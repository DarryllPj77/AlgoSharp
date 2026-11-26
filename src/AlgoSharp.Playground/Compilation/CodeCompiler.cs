using System.Collections.Immutable;
using AlgoSharp.Tracing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AlgoSharp.Playground.Compilation;

public sealed record Diagnostic(string Severity, string Message, int Line, int Column, string Code);

public sealed record CompileResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    byte[]? Assembly,
    string? TypeName,
    string? MethodName,
    SyntaxTree? Tree)
{
    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == "error");
}

/// <summary>
/// The method the problem calls, e.g. int[] TwoSum(int[], int). For design problems
/// (<see cref="DesignClass"/> set) the class is constructed and <see cref="PreferredName"/> is called repeatedly.
/// </summary>
public sealed record EntrySignature(string PreferredName, ITypeSymbolMatcher[] Parameters, string Description, string? DesignClass = null);

public delegate bool ITypeSymbolMatcher(ITypeSymbol type);

/// <summary>
/// Compiles user code with Roslyn. Compilation never runs the code: it produces diagnostics and,
/// on success, an instrumented assembly that is only ever executed inside the sandbox runner.
/// </summary>
public static class CodeCompiler
{
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Text;
        """;

    /// <summary>LeetCode's predefined node types, added unless the user declares their own.</summary>
    private static readonly (string Name, string Source)[] Definitions =
    [
        ("ListNode", """
            public class ListNode
            {
                public int val;
                public ListNode next;
                public ListNode(int val = 0, ListNode next = null) { this.val = val; this.next = next; }
            }
            """),
        ("TreeNode", """
            public class TreeNode
            {
                public int val;
                public TreeNode left;
                public TreeNode right;
                public TreeNode(int val = 0, TreeNode left = null, TreeNode right = null) { this.val = val; this.left = left; this.right = right; }
            }
            """),
    ];

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly CSharpCompilationOptions Options = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Debug,
        allowUnsafe: false,
        nullableContextOptions: NullableContextOptions.Disable,
        concurrentBuild: false);

    public static CompileResult Compile(string source, EntrySignature entry)
    {
        if (source.Length > 20_000) return Fail("Code is too long (20,000 characters max).");

        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Solution.cs");
        var usings = CSharpSyntaxTree.ParseText(GlobalUsings, ParseOptions, path: "GlobalUsings.cs");
        var declared = tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).ToHashSet();
        var support = new List<SyntaxTree> { usings };
        foreach (var (name, src) in Definitions)
        {
            if (!declared.Contains(name)) support.Add(CSharpSyntaxTree.ParseText(src, ParseOptions, path: name + ".cs"));
        }
        var compilation = CSharpCompilation.Create("UserSolution", [tree, .. support], References.Value, Options);
        var model = compilation.GetSemanticModel(tree);

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree == tree)
            .Select(ToDiagnostic)
            .ToList();
        diagnostics.AddRange(SafetyAnalyzer.Check(tree, model));

        if (diagnostics.Any(d => d.Severity == "error")) return new(false, Sorted(diagnostics), null, null, null, tree);

        var (type, method, entryError) = FindEntry(tree, model, entry);
        if (entryError is not null)
        {
            diagnostics.Add(new Diagnostic("error", entryError, 1, 1, "AS0100"));
            return new(false, Sorted(diagnostics), null, null, null, tree);
        }

        // Instrument the original tree, then compile the instrumented version.
        SyntaxNode instrumentedRoot;
        try
        {
            instrumentedRoot = new Instrumenter(model).Visit(tree.GetRoot())!;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidCastException)
        {
            diagnostics.Add(new Diagnostic("error", $"The visualizer could not instrument this code ({ex.Message}). Try simplifying the construct on the reported line.", 1, 1, "AS0200"));
            return new(false, Sorted(diagnostics), null, null, null, tree);
        }
        var instrumented = CSharpSyntaxTree.Create((CSharpSyntaxNode)instrumentedRoot, ParseOptions, "Solution.instrumented.cs");
        var traced = CSharpCompilation.Create("UserSolution", [instrumented, .. support], References.Value, Options);

        using var stream = new MemoryStream();
        var emit = traced.Emit(stream);
        if (!emit.Success)
        {
            var first = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            diagnostics.Add(new Diagnostic("error",
                $"This code compiles, but the visualizer cannot trace one of its constructs yet ({first?.GetMessage()}). Please report it; try writing that part differently.",
                1, 1, "AS0201"));
            return new(false, Sorted(diagnostics), null, null, null, tree);
        }
        return new(true, Sorted(diagnostics), stream.ToArray(), type, method, tree);
    }

    private static List<Diagnostic> Sorted(List<Diagnostic> d) => [.. d.OrderBy(x => x.Severity == "error" ? 0 : 1).ThenBy(x => x.Line)];

    private static CompileResult Fail(string message) => new(false, [new Diagnostic("error", message, 1, 1, "AS0001")], null, null, null, null);

    private static Diagnostic ToDiagnostic(Microsoft.CodeAnalysis.Diagnostic d)
    {
        var pos = d.Location.GetLineSpan().StartLinePosition;
        return new Diagnostic(d.Severity == DiagnosticSeverity.Error ? "error" : "warning", d.GetMessage(), pos.Line + 1, pos.Character + 1, d.Id);
    }

    /// <summary>Finds the method to call: preferred name first, otherwise any method with the right parameter types.</summary>
    private static (string? Type, string? Method, string? Error) FindEntry(SyntaxTree tree, SemanticModel model, EntrySignature entry)
    {
        if (entry.DesignClass is { } designClass)
        {
            var cls = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Select(c => model.GetDeclaredSymbol(c)).OfType<INamedTypeSymbol>()
                .FirstOrDefault(c => c.Name == designClass);
            if (cls is null) return (null, null, $"Keep the class named {designClass}: {entry.Description}.");
            var m = cls.GetMembers(entry.PreferredName).OfType<IMethodSymbol>()
                .FirstOrDefault(x => x.DeclaredAccessibility == Accessibility.Public && !x.IsStatic && x.Parameters.Length == entry.Parameters.Length
                                     && x.Parameters.Select((p, i) => entry.Parameters[i](p.Type)).All(ok => ok));
            if (m is null) return (null, null, $"{designClass} needs a public method {entry.Description}.");
            return (cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""), m.Name, null);
        }
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(m => model.GetDeclaredSymbol(m))
            .OfType<IMethodSymbol>()
            .Where(m => m.ContainingType.TypeKind == TypeKind.Class && !m.ContainingType.IsGenericType && m.Parameters.Length == entry.Parameters.Length
                        && m.Parameters.Select((p, i) => p.RefKind == RefKind.None && entry.Parameters[i](p.Type)).All(ok => ok))
            .ToList();
        var chosen = methods.FirstOrDefault(m => string.Equals(m.Name, entry.PreferredName, StringComparison.OrdinalIgnoreCase))
                     ?? (methods.Count == 1 ? methods[0] : null);
        if (chosen is null)
        {
            return methods.Count > 1
                ? (null, null, $"Several methods match {entry.Description}. Name the one to test {entry.PreferredName}.")
                : (null, null, $"Couldn't find a method like {entry.Description} inside a class.");
        }
        if (!chosen.IsStatic && chosen.ContainingType.InstanceConstructors.All(c => c.Parameters.Length > 0))
        {
            return (null, null, $"{chosen.ContainingType.Name} needs a parameterless constructor so it can be created for each test.");
        }
        return (chosen.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", ""), chosen.Name, null);
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        string[] wanted =
        [
            "System.Private.CoreLib", "System.Runtime", "System.Collections", "System.Linq", "System.Console",
            "System.Runtime.Extensions", "System.Text.RegularExpressions", "System.Memory", "netstandard",
            "System.Collections.NonGeneric", "System.Numerics", "System.Runtime.Numerics",
        ];
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var refs = tpa.Where(p => wanted.Contains(Path.GetFileNameWithoutExtension(p)))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Tr).Assembly.Location));
        return [.. refs];
    }
}
