using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AlgoSharp.Playground.Library;

public sealed record SignatureParam(string Type, string Name, ArgKind Kind);

public sealed record ParsedSignature(string ReturnType, string Name, IReadOnlyList<SignatureParam> Params)
{
    public string Display => $"{ReturnType} {Name}({string.Join(", ", Params.Select(p => $"{p.Type} {p.Name}"))})";
}

/// <summary>Reads a C# method signature and maps its types to the inputs the runner can build.</summary>
public static class SignatureParser
{
    /// <summary>Parameter types tests can be written for (JSON in, deserialized by the runner).</summary>
    private static readonly Dictionary<string, ArgKind> Supported = new()
    {
        ["int"] = ArgKind.Int, ["uint"] = ArgKind.UInt, ["long"] = ArgKind.Json, ["double"] = ArgKind.Json,
        ["bool"] = ArgKind.Json, ["char"] = ArgKind.Json, ["string"] = ArgKind.String,
        ["int[]"] = ArgKind.IntArray, ["long[]"] = ArgKind.Json, ["double[]"] = ArgKind.Json, ["bool[]"] = ArgKind.Json,
        ["char[]"] = ArgKind.Json, ["string[]"] = ArgKind.Json, ["int[][]"] = ArgKind.Json, ["char[][]"] = ArgKind.Json,
        ["string[][]"] = ArgKind.Json, ["List<int>"] = ArgKind.Json, ["List<string>"] = ArgKind.Json,
        ["List<List<int>>"] = ArgKind.Json, ["IList<int>"] = ArgKind.Json, ["ListNode"] = ArgKind.LinkedList, ["TreeNode"] = ArgKind.Tree,
    };

    public static IReadOnlyCollection<string> SupportedTypes => Supported.Keys;

    /// <summary>Parses e.g. "public int CountLocalMaximums(int[] nums)". Throws <see cref="FormatException"/> with a readable reason.</summary>
    public static ParsedSignature Parse(string signature)
    {
        string text = signature.Trim().TrimEnd(';', '{').Trim();
        if (text.Length == 0) throw new FormatException("The method signature is empty.");
        if (!text.Contains('(')) throw new FormatException("A signature needs parentheses, e.g. public int Solve(int[] nums).");
        if (CSharpSyntaxTree.ParseText($"class __S {{ {text} {{ throw null; }} }}").GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault() is not { } method)
        {
            throw new FormatException($"“{signature}” isn't a C# method signature.");
        }
        if (method.TypeParameterList is not null) throw new FormatException("Generic methods aren't supported.");
        var ps = new List<SignatureParam>();
        foreach (var p in method.ParameterList.Parameters)
        {
            if (p.Modifiers.Count > 0) throw new FormatException($"Parameter {p.Identifier.Text}: ref/out/params aren't supported.");
            string type = Normalize(p.Type?.ToString() ?? "");
            if (!Supported.TryGetValue(type, out var kind))
            {
                throw new FormatException($"Parameter type {type} isn't supported yet. Supported: {string.Join(", ", Supported.Keys)}.");
            }
            ps.Add(new SignatureParam(type, p.Identifier.Text, kind));
        }
        string ret = Normalize(method.ReturnType.ToString());
        if (ret == "void") throw new FormatException("The method must return its answer (void methods can't be graded).");
        return new ParsedSignature(ret, method.Identifier.Text, ps);
    }

    public static bool TryParse(string? signature, out ParsedSignature? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (string.IsNullOrWhiteSpace(signature))
        {
            error = "No method signature.";
            return false;
        }
        try
        {
            parsed = Parse(signature);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string Normalize(string type) => type.Replace(" ", "").Replace("System.Collections.Generic.", "")
        .Replace("Int32", "int").Replace("String", "string").Replace("Boolean", "bool");

    /// <summary>Compares a Roslyn type with a signature type text.</summary>
    public static bool Matches(ITypeSymbol symbol, string type) =>
        Normalize(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)) == type;
}
