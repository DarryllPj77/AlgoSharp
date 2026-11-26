using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AlgoSharp.Playground.Compilation;

/// <summary>
/// Rejects APIs and constructs that make no sense for a problem solution, and constructs the
/// visualizer cannot trace faithfully. This is a usability and defence-in-depth check; it is
/// NOT the security boundary — the sandbox container is.
/// </summary>
internal static class SafetyAnalyzer
{
    private static readonly string[] BlockedNamespaces =
    [
        "System.IO", "System.Net", "System.Reflection", "System.Diagnostics", "System.Threading",
        "System.Runtime", "System.Security", "System.Resources", "Microsoft", "AlgoSharp",
    ];

    private static readonly HashSet<string> BlockedTypes =
    [
        "System.Environment", "System.AppDomain", "System.Activator", "System.GC", "System.Type",
        "System.AppContext", "System.Delegate",
    ];

    private static readonly HashSet<string> BlockedConsoleMembers = ["Read", "ReadLine", "ReadKey", "OpenStandardInput", "OpenStandardOutput", "OpenStandardError", "SetIn", "SetOut", "SetError", "In"];

    public static IEnumerable<Diagnostic> Check(SyntaxTree tree, SemanticModel model)
    {
        var root = tree.GetRoot();
        var reported = new HashSet<(int, string)>();

        Diagnostic Error(SyntaxNode node, string message)
        {
            var pos = node.GetLocation().GetLineSpan().StartLinePosition;
            return new Diagnostic("error", message, pos.Line + 1, pos.Character + 1, "AS0300");
        }

        if (root is CompilationUnitSyntax cu && cu.Members.OfType<GlobalStatementSyntax>().Any())
        {
            yield return Error(cu.Members.OfType<GlobalStatementSyntax>().First(), "Top-level statements aren't supported here. Put your solution in a class, e.g. public class Solution { ... }.");
        }

        foreach (var node in root.DescendantNodesAndTokens())
        {
            if (node.IsToken)
            {
                var token = node.AsToken();
                string? unsupported = token.Kind() switch
                {
                    SyntaxKind.UnsafeKeyword or SyntaxKind.FixedKeyword or SyntaxKind.StackAllocKeyword => "unsafe code",
                    SyntaxKind.GotoKeyword => "goto",
                    SyntaxKind.AsyncKeyword or SyntaxKind.AwaitKeyword => "async/await",
                    SyntaxKind.YieldKeyword => "yield (iterators)",
                    SyntaxKind.ExternKeyword => "extern methods",
                    SyntaxKind.VolatileKeyword => "volatile fields",
                    _ => null,
                };
                if (unsupported is not null && token.Parent is not null && reported.Add((token.SpanStart, unsupported)))
                {
                    yield return Error(token.Parent, $"This uses {unsupported}, which the visualizer doesn't support.");
                }
                continue;
            }

            var syntax = node.AsNode()!;
            if (syntax is IdentifierNameSyntax { Identifier.Text: "dynamic" } dyn && model.GetTypeInfo(dyn).Type is IDynamicTypeSymbol)
            {
                yield return Error(syntax, "dynamic is not supported by the visualizer.");
                continue;
            }
            if (syntax is not (IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax)) continue;

            var symbol = model.GetSymbolInfo(syntax).Symbol;
            if (symbol is null) continue;
            var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
            string ns = (symbol as INamespaceSymbol ?? type?.ContainingNamespace ?? symbol.ContainingNamespace)?.ToDisplayString() ?? "";
            string? blocked = null;
            if (BlockedNamespaces.Any(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal))) blocked = ns;
            else if (type is not null && BlockedTypes.Contains(type.ToDisplayString())) blocked = type.ToDisplayString();
            else if (type?.ToDisplayString() == "System.Console" && BlockedConsoleMembers.Contains(symbol.Name)) blocked = $"Console.{symbol.Name}";
            else if (symbol.Name == "GetType" && symbol is IMethodSymbol) blocked = "GetType()";

            if (blocked is not null)
            {
                int line = syntax.GetLocation().GetLineSpan().StartLinePosition.Line;
                if (reported.Add((line, blocked)))
                {
                    yield return Error(syntax, $"{blocked} isn't available in AlgoSharp's sandbox. Solutions can use System, System.Collections.Generic, System.Linq, System.Text and Console.WriteLine.");
                }
            }
        }
    }
}
