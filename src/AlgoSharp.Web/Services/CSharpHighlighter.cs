using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AlgoSharp.Web.Services;

/// <summary>A small, dependency-free C# syntax highlighter producing span-wrapped HTML.</summary>
public static partial class CSharpHighlighter
{
    private static readonly HashSet<string> Keywords =
    [
        "public", "private", "static", "void", "int", "bool", "return", "for", "while", "if", "else",
        "new", "true", "false", "null", "class", "sealed", "break", "continue", "throw", "this",
        "readonly", "var", "foreach", "in", "out", "ref", "string", "double", "internal", "using",
    ];

    public static string Highlight(string line)
    {
        var sb = new StringBuilder();
        foreach (Match m in TokenRegex().Matches(line))
        {
            string text = WebUtility.HtmlEncode(m.Value);
            string? cls = m switch
            {
                _ when m.Groups["comment"].Success => "tk-c",
                _ when m.Groups["str"].Success => "tk-s",
                _ when m.Groups["num"].Success => "tk-n",
                _ when m.Groups["id"].Success && Keywords.Contains(m.Value) => "tk-k",
                _ when m.Groups["id"].Success && m.Groups["call"].Success => "tk-f",
                _ when m.Groups["id"].Success && char.IsUpper(m.Value[0]) => "tk-t",
                _ when m.Groups["op"].Success => "tk-o",
                _ => null,
            };
            sb.Append(cls is null ? text : $"<span class=\"{cls}\">{text}</span>");
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"(?<comment>//.*$)|(?<str>\$?""(?:[^""\\]|\\.)*"")|(?<num>\b\d+\b)|(?<id>[A-Za-z_]\w*)(?<call>(?=\s*\())?|(?<op>[+\-*/%=<>!&|?:]+)|(?<ws>\s+)|(?<other>.)")]
    private static partial Regex TokenRegex();
}
