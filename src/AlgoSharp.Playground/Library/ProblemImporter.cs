using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlgoSharp.Playground.Library;

public sealed record ImportIssue(string Path, string Message, bool Blocking);

/// <summary>A problem extracted for review, with any issues found while reading it.</summary>
public sealed record ImportedProblem(UserProblem Problem, IReadOnlyList<ImportIssue> Issues);

public sealed record ImportResult(string Kind, string? Collection, IReadOnlyList<ImportedProblem> Problems, IReadOnlyList<ImportIssue> Issues)
{
    public bool Ok => Problems.Count > 0 && !Issues.Any(i => i.Blocking);
}

/// <summary>
/// Turns pasted material into problems for review. AlgoSharp JSON is detected first; anything
/// else is parsed as lesson text with deterministic rules. Nothing is invented: unknown fields stay
/// empty, suggested signatures are flagged, and all tests start unverified unless the JSON says otherwise.
/// </summary>
public static partial class ProblemImporter
{
    public static ImportResult Import(string pasted)
    {
        string text = (pasted ?? "").Trim();
        if (text.Length == 0) return new("empty", null, [], [new("", "Nothing to import: the text is empty.", true)]);
        if (text.Length > 200_000) return new("empty", null, [], [new("", "That's too much text to import at once (200,000 characters max).", true)]);
        string unfenced = StripFence(text);
        if (unfenced.StartsWith('{') || unfenced.StartsWith('[')) return ImportJson(unfenced);
        return ImportText(text);
    }

    // ================================================================ AlgoSharp JSON

    public static ImportResult ImportJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(StripFence(json.Trim()), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            return new("json", null, [], [new($"line {ex.LineNumber + 1}", $"Invalid JSON: {ex.Message.Split(" Path:")[0]}", true)]);
        }

        var issues = new List<ImportIssue>();
        var root = doc.RootElement;
        string? collection = null;
        JsonElement problemsElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            problemsElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("problems", out var ps))
        {
            if (root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() != ImportSchema.Format)
                issues.Add(new("format", $"Expected \"{ImportSchema.Format}\" but found \"{f.GetString()}\". Importing anyway.", false));
            if (root.TryGetProperty("version", out var v))
            {
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int ver)) issues.Add(new("version", "must be an integer.", true));
                else if (ver > ImportSchema.Version) issues.Add(new("version", $"Version {ver} is newer than this app supports ({ImportSchema.Version}).", true));
            }
            collection = Str(root, "collection", "collection", issues);
            problemsElement = ps;
            if (ps.ValueKind != JsonValueKind.Array) return new("json", collection, [], [.. issues, new("problems", "must be an array.", true)]);
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("title", out _))
        {
            problemsElement = JsonDocument.Parse("[" + root.GetRawText() + "]").RootElement;
        }
        else
        {
            return new("json", null, [], [new("", "Expected an object with a \"problems\" array (AlgoSharp format), an array of problems, or a single problem object.", true)]);
        }

        var problems = new List<ImportedProblem>();
        int index = 0;
        foreach (var p in problemsElement.EnumerateArray())
        {
            problems.Add(ReadJsonProblem(p, $"problems[{index++}]", collection));
        }
        if (problems.Count == 0) issues.Add(new("problems", "contains no problems.", true));
        return new("json", collection, problems, issues);
    }

    private static ImportedProblem ReadJsonProblem(JsonElement p, string path, string? collection)
    {
        var issues = new List<ImportIssue>();
        if (p.ValueKind != JsonValueKind.Object) return new(new UserProblem(), [new(path, "must be an object.", true)]);

        foreach (var prop in p.EnumerateObject())
        {
            if (!ImportSchema.Problem.Any(f => f.Name == prop.Name)) issues.Add(new($"{path}.{prop.Name}", "Unknown field, ignored.", false));
        }
        string title = Str(p, "title", $"{path}.title", issues) ?? "";
        string description = Str(p, "description", $"{path}.description", issues) ?? "";
        if (title.Trim().Length == 0) issues.Add(new($"{path}.title", "is required.", true));
        if (description.Trim().Length == 0) issues.Add(new($"{path}.description", "is required.", true));

        int? level = null;
        if (p.TryGetProperty("level", out var lv) && lv.ValueKind != JsonValueKind.Null)
        {
            if (lv.ValueKind == JsonValueKind.Number && lv.TryGetInt32(out int l)) level = l;
            else issues.Add(new($"{path}.level", "must be an integer.", false));
        }
        string? difficulty = Str(p, "difficulty", $"{path}.difficulty", issues);
        if (difficulty is not null && NormalizeDifficulty(difficulty) is not { } d) issues.Add(new($"{path}.difficulty", $"\"{difficulty}\" isn't Easy, Medium or Hard.", false));
        string? signature = Str(p, "signature", $"{path}.signature", issues);

        ParsedSignature? sig = null;
        if (signature is not null && !SignatureParser.TryParse(signature, out sig, out var sigError))
            issues.Add(new($"{path}.signature", sigError!, false));

        var examples = new List<UserExample>();
        var tests = new List<UserTest>();
        if (p.TryGetProperty("examples", out var exs))
        {
            if (exs.ValueKind != JsonValueKind.Array) issues.Add(new($"{path}.examples", "must be an array.", false));
            else
            {
                int i = 0;
                foreach (var e in exs.EnumerateArray())
                {
                    string ep = $"{path}.examples[{i++}]";
                    if (e.ValueKind != JsonValueKind.Object) { issues.Add(new(ep, "must be an object.", false)); continue; }
                    string input = Str(e, "input", $"{ep}.input", issues) ?? "";
                    string output = ValueText(e, "output") ?? "";
                    if (input.Length == 0) issues.Add(new($"{ep}.input", "is required.", false));
                    if (!e.TryGetProperty("output", out _)) issues.Add(new($"{ep}.output", "is required.", false));
                    examples.Add(new UserExample { Input = input, Output = output, Explanation = Str(e, "explanation", $"{ep}.explanation", issues) });
                    if (sig is not null)
                    {
                        var args = e.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                            ? ReadArgs(a, sig, $"{ep}.args", issues)
                            : SplitArgs(input, sig);
                        string? expected = NormalizeExpected(output, sig.ReturnType);
                        if (args is not null && expected is not null) tests.Add(new UserTest { Args = args, Expected = expected, Verified = false });
                    }
                }
            }
        }
        if (p.TryGetProperty("tests", out var ts))
        {
            if (ts.ValueKind != JsonValueKind.Array) issues.Add(new($"{path}.tests", "must be an array.", false));
            else if (sig is null && ts.GetArrayLength() > 0) issues.Add(new($"{path}.tests", "need a valid signature to be used; kept for reference.", false));
            int i = 0;
            foreach (var t in ts.ValueKind == JsonValueKind.Array ? ts.EnumerateArray().ToList() : [])
            {
                string tp = $"{path}.tests[{i++}]";
                if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("args", out var a) || a.ValueKind != JsonValueKind.Array)
                {
                    issues.Add(new($"{tp}.args", "is required and must be an array of JSON values.", false));
                    continue;
                }
                if (!t.TryGetProperty("expected", out var ex)) { issues.Add(new($"{tp}.expected", "is required.", false)); continue; }
                var args = sig is null ? a.EnumerateArray().Select(x => x.GetRawText()).ToList() : ReadArgs(a, sig, $"{tp}.args", issues);
                string? expected = sig is null ? ex.GetRawText() : NormalizeExpected(ex.ValueKind == JsonValueKind.String ? ex.GetString()! : ex.GetRawText(), sig.ReturnType, ex.ValueKind == JsonValueKind.String);
                bool verified = t.TryGetProperty("verified", out var vf) && vf.ValueKind == JsonValueKind.True;
                if (args is not null && expected is not null) tests.Add(new UserTest { Args = args, Expected = expected, Verified = verified });
                else if (expected is null) issues.Add(new($"{tp}.expected", "couldn't be read as JSON.", false));
            }
        }

        var problem = new UserProblem
        {
            Title = title.Trim(),
            Level = level,
            Difficulty = difficulty is null ? null : NormalizeDifficulty(difficulty) ?? difficulty,
            Category = Str(p, "category", $"{path}.category", issues),
            Concepts = StrArray(p, "concepts", $"{path}.concepts", issues),
            Collection = collection ?? "",
            Description = description.Trim(),
            InputSpec = Str(p, "input", $"{path}.input", issues),
            OutputSpec = Str(p, "output", $"{path}.output", issues),
            Examples = examples,
            Constraints = StrArray(p, "constraints", $"{path}.constraints", issues),
            Instructions = Str(p, "instructions", $"{path}.instructions", issues),
            Signature = sig?.Display is { } s ? "public " + s : signature,
            StarterCode = Str(p, "starterCode", $"{path}.starterCode", issues),
            Tests = tests,
            Source = "algosharp-json",
        };
        return new(problem, issues);
    }

    private static List<string>? ReadArgs(JsonElement array, ParsedSignature sig, string path, List<ImportIssue> issues)
    {
        var values = array.EnumerateArray().ToList();
        if (values.Count != sig.Params.Count)
        {
            issues.Add(new(path, $"has {values.Count} value(s) but the signature has {sig.Params.Count} parameter(s).", false));
            return null;
        }
        // A JSON string holding JSON (e.g. "[1, 2]") is accepted for non-string parameters.
        return values.Select((v, i) => v.ValueKind == JsonValueKind.String && sig.Params[i].Type is not ("string" or "char") && TryJson(v.GetString()!) is { } inner ? inner : v.GetRawText()).ToList();
    }

    private static string? Str(JsonElement o, string name, string path, List<ImportIssue> issues)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        issues.Add(new(path, "must be a string.", false));
        return v.GetRawText();
    }

    private static string? ValueText(JsonElement o, string name) =>
        !o.TryGetProperty(name, out var v) ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();

    private static List<string> StrArray(JsonElement o, string name, string path, List<ImportIssue> issues)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return [];
        if (v.ValueKind == JsonValueKind.String) return [.. SplitList(v.GetString()!)];
        if (v.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new(path, "must be an array of strings.", false));
            return [];
        }
        return [.. v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.GetRawText())];
    }

    // ================================================================ lesson text

    private static readonly string[] Labels =
    [
        "concepts", "concept", "topics", "topic", "tags", "skills", "category", "categories", "difficulty", "level",
        "description", "problem", "problem statement", "task", "goal", "objective", "instructions", "requirements", "requirement",
        "input", "inputs", "sample input", "example input", "output", "outputs", "expected output", "sample output", "example output", "return",
        "explanation", "constraints", "constraint", "notes", "note", "hint", "hints", "method signature", "signature", "function signature",
        "example", "examples", "starter code",
    ];

    public static ImportResult ImportText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var headers = new List<(int Line, int? Level, string Title, string? Difficulty)>();
        for (int i = 0; i < lines.Length; i++)
        {
            var m = HeaderRegex().Match(lines[i]);
            if (m.Success)
            {
                int? level = int.TryParse(m.Groups["num"].Value, out int n) ? n : null;
                var (title, diff) = SplitDifficulty(Clean(m.Groups["title"].Value));
                headers.Add((i, level, title, diff));
            }
        }

        var issues = new List<ImportIssue>();
        string? collection = null;
        var blocks = new List<(int? Level, string Title, string? Difficulty, string[] Body)>();
        if (headers.Count == 0)
        {
            // One exercise: the first non-empty line is the title unless it's a field.
            int first = Array.FindIndex(lines, l => l.Trim().Length > 0);
            string firstLine = Clean(lines[first]);
            bool isField = LabelRegex().Match(lines[first]) is { Success: true } lm && Labels.Contains(lm.Groups["label"].Value.Trim().ToLowerInvariant());
            var (title, diff) = SplitDifficulty(isField ? "" : firstLine);
            blocks.Add((null, title, diff, isField ? lines : lines[(first + 1)..]));
            if (isField) issues.Add(new("title", "No title found; please add one.", false));
        }
        else
        {
            var preamble = lines[..headers[0].Line].Select(Clean).Where(l => l.Length > 0).ToList();
            if (preamble.Count > 0) collection = preamble[0].Length <= 80 ? preamble[0] : null;
            for (int h = 0; h < headers.Count; h++)
            {
                int end = h + 1 < headers.Count ? headers[h + 1].Line : lines.Length;
                blocks.Add((headers[h].Level, headers[h].Title, headers[h].Difficulty, lines[(headers[h].Line + 1)..end]));
            }
        }

        var problems = blocks.Select((b, i) => ReadTextProblem(b.Level, b.Title, b.Difficulty, b.Body, collection, $"problem {i + 1}")).ToList();
        return new("text", collection, problems, issues);
    }

    private static ImportedProblem ReadTextProblem(int? level, string title, string? headerDifficulty, string[] body, string? collection, string path)
    {
        var issues = new List<ImportIssue>();
        var fields = new Dictionary<string, StringBuilder>();
        var description = new StringBuilder();
        var examples = new List<(StringBuilder Input, StringBuilder Output, StringBuilder Explanation)>();
        string? current = null;
        bool inFence = false;

        void Append(StringBuilder sb, string line)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }

        StringBuilder Target(string field)
        {
            switch (field)
            {
                case "input":
                    examples.Add((new(), new(), new()));
                    return examples[^1].Input;
                case "output":
                    if (examples.Count == 0 || examples[^1].Output.Length > 0) examples.Add((new(), new(), new()));
                    return examples[^1].Output;
                case "explanation" when examples.Count > 0:
                    return examples[^1].Explanation;
                default:
                    return fields.TryGetValue(field, out var sb) ? sb : fields[field] = new StringBuilder();
            }
        }

        StringBuilder? currentBuilder = null;
        foreach (var raw in body)
        {
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence)
            {
                // "Input: [1, 2] → Output: 3" on one line.
                var inline = InlineIoRegex().Match(line);
                if (inline.Success)
                {
                    examples.Add((new(Clean(inline.Groups["in"].Value)), new(Clean(inline.Groups["out"].Value)), new()));
                    current = "output";
                    currentBuilder = examples[^1].Output;
                    continue;
                }
                var m = LabelRegex().Match(line);
                string label = m.Success ? m.Groups["label"].Value.Trim().ToLowerInvariant() : "";
                if (m.Success && Labels.Contains(label))
                {
                    current = Canonical(label);
                    if (current == "example")
                    {
                        currentBuilder = null;
                        string rest = Clean(m.Groups["value"].Value);
                        if (rest.Length > 0 && LabelRegex().Match(rest) is { Success: true } nested && Canonical(nested.Groups["label"].Value.Trim().ToLowerInvariant()) == "input")
                        {
                            currentBuilder = Target("input");
                            Append(currentBuilder, Clean(nested.Groups["value"].Value));
                            current = "input";
                        }
                        continue;
                    }
                    currentBuilder = Target(current);
                    string value = Clean(m.Groups["value"].Value);
                    if (value.Length > 0)
                    {
                        Append(currentBuilder, value);
                        // Fields written on one line end there; the next unlabeled line belongs to the statement.
                        if (current is "concepts" or "category" or "difficulty" or "level" or "input" or "output" or "signature")
                        {
                            currentBuilder = null;
                            current = null;
                        }
                    }
                    continue;
                }
            }
            string text = inFence ? line : Clean(line);
            if (text.Length == 0)
            {
                // A blank line ends single-line fields but keeps multi-line ones (code, description).
                if (current is "concepts" or "category" or "difficulty" or "level" or "output" or "input") { current = null; currentBuilder = null; }
                else if (currentBuilder is not null && current is not ("signature" or "starter")) Append(currentBuilder, "");
                continue;
            }
            if (currentBuilder is null) Append(description, text);
            else Append(currentBuilder, text);
        }

        string? Field(string name) => fields.TryGetValue(name, out var sb) && sb.ToString().Trim() is { Length: > 0 } s ? s : null;

        string desc = description.ToString().Trim();
        if (Field("description") is { } d) desc = desc.Length > 0 ? desc + "\n\n" + d : d;
        if (desc.Length == 0 && Field("instructions") is { } ins) desc = ins;
        if (desc.Length == 0) issues.Add(new($"{path}.description", "No description found. Add one before saving.", true));
        if (title.Length == 0) issues.Add(new($"{path}.title", "No title found. Add one before saving.", true));

        var exampleList = examples
            .Where(e => e.Input.Length > 0 || e.Output.Length > 0)
            .Select(e => new UserExample { Input = e.Input.ToString().Trim(), Output = e.Output.ToString().Trim(), Explanation = e.Explanation.Length > 0 ? e.Explanation.ToString().Trim() : null })
            .ToList();
        foreach (var (e, i) in exampleList.Select((e, i) => (e, i)))
        {
            if (e.Input.Length == 0) issues.Add(new($"{path}.examples[{i}]", "has an output but no input.", false));
            if (e.Output.Length == 0) issues.Add(new($"{path}.examples[{i}]", "has an input but no output.", false));
        }

        string? difficulty = Field("difficulty") is { } fd ? NormalizeDifficulty(fd) ?? fd : headerDifficulty;
        if (level is null && Field("level") is { } fl && int.TryParse(Regex.Match(fl, @"\d+").Value, out int lvl)) level = lvl;

        // Signature: as given in the source, or suggested from the examples (flagged).
        string? signature = Field("signature");
        bool suggested = false;
        if (signature is not null && !SignatureParser.TryParse(signature, out _, out var sigErr))
        {
            issues.Add(new($"{path}.signature", sigErr!, false));
        }
        if (signature is null && SuggestSignature(title, exampleList) is { } guess)
        {
            signature = guess;
            suggested = true;
        }
        var tests = new List<UserTest>();
        if (signature is not null && SignatureParser.TryParse(signature, out var sig, out _))
        {
            foreach (var e in exampleList)
            {
                if (SplitArgs(e.Input, sig!) is { } args && NormalizeExpected(e.Output, sig!.ReturnType) is { } expected)
                    tests.Add(new UserTest { Args = args, Expected = expected, Verified = false });
            }
        }

        var problem = new UserProblem
        {
            Title = title,
            Level = level,
            Difficulty = difficulty,
            Category = Field("category"),
            Concepts = Field("concepts") is { } c ? [.. SplitList(c)] : [],
            Collection = collection ?? "",
            Description = desc,
            Examples = exampleList,
            Constraints = Field("constraints") is { } cs ? [.. cs.Split('\n').Select(x => x.Trim().TrimStart('-', '*', '•').Trim()).Where(x => x.Length > 0)] : [],
            Instructions = Field("instructions") is { } i2 && i2 != desc ? i2 : null,
            Signature = signature,
            SignatureSuggested = suggested,
            StarterCode = Field("starter"),
            Tests = tests,
            Source = "text",
        };
        return new(problem, issues);
    }

    private static string Canonical(string label) => label switch
    {
        "concept" or "topics" or "topic" or "tags" or "skills" => "concepts",
        "categories" => "category",
        "problem" or "problem statement" or "task" or "goal" or "objective" => "description",
        "requirements" or "requirement" or "notes" or "note" or "hint" or "hints" => "instructions",
        "inputs" or "sample input" or "example input" => "input",
        "outputs" or "expected output" or "sample output" or "example output" or "return" => "output",
        "constraint" => "constraints",
        "method signature" or "function signature" => "signature",
        "examples" => "example",
        "starter code" => "starter",
        _ => label,
    };

    // ================================================================ shared helpers

    /// <summary>Splits "nums = [1, 2], target = 3" (or a bare value for one parameter) into JSON values per parameter.</summary>
    public static List<string>? SplitArgs(string input, ParsedSignature sig)
    {
        string text = input.Trim();
        if (sig.Params.Count == 0) return [];
        var named = NamedArgRegex().Matches(text);
        var parts = TopLevelSplit(text);
        List<string> values;
        if (parts.Count == sig.Params.Count && parts.All(p => NamedArgRegex().IsMatch(p)))
        {
            var byName = parts.Select(p => NamedArgRegex().Match(p)).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim());
            if (!sig.Params.All(p => byName.ContainsKey(p.Name)))
            {
                values = [.. byName.Values];
            }
            else values = [.. sig.Params.Select(p => byName[p.Name])];
        }
        else if (sig.Params.Count == 1)
        {
            values = [NamedArgRegex().Match(text) is { Success: true } m && parts.Count == 1 ? m.Groups["value"].Value.Trim() : text];
        }
        else if (parts.Count == sig.Params.Count)
        {
            values = [.. parts.Select(p => NamedArgRegex().Match(p) is { Success: true } m ? m.Groups["value"].Value.Trim() : p.Trim())];
        }
        else return null;

        var result = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            string v = values[i];
            if (sig.Params[i].Type == "string" && !v.StartsWith('"')) v = JsonSerializer.Serialize(v);
            if (TryJson(v) is not { } json) return null;
            result.Add(json);
        }
        return result;
    }

    /// <summary>Expected output as JSON text, or null if it can't be read for this return type.</summary>
    public static string? NormalizeExpected(string output, string returnType, bool alreadyString = false)
    {
        string text = output.Trim();
        if (returnType is "string" or "char")
        {
            if (alreadyString) return JsonSerializer.Serialize(text);
            return text.StartsWith('"') && TryJson(text) is { } q ? q : JsonSerializer.Serialize(text.Trim('\''));
        }
        if (returnType == "bool") text = text.ToLowerInvariant();
        return TryJson(text);
    }

    private static string? TryJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Suggests "public &lt;ret&gt; TitleInPascalCase(&lt;types&gt;)" from the first example's values. Flagged as suggested.</summary>
    public static string? SuggestSignature(string title, IReadOnlyList<UserExample> examples)
    {
        if (examples.Count == 0 || title.Trim().Length == 0) return null;
        var first = examples[0];
        var parts = TopLevelSplit(first.Input);
        var ps = new List<string>();
        int k = 0;
        foreach (var part in parts)
        {
            var m = NamedArgRegex().Match(part);
            string value = m.Success ? m.Groups["value"].Value.Trim() : part.Trim();
            if (TryJson(value) is not { } json) return null;
            string? type = TypeOf(JsonDocument.Parse(json).RootElement);
            if (type is null) return null;
            string name = m.Success ? m.Groups["name"].Value : DefaultName(type, k++);
            ps.Add($"{type} {name}");
        }
        if (TryJson(first.Output.Trim().ToLowerInvariant() is "true" or "false" ? first.Output.Trim().ToLowerInvariant() : first.Output.Trim()) is not { } outJson) return null;
        string? ret = TypeOf(JsonDocument.Parse(outJson).RootElement);
        if (ret is null) return null;
        string method = string.Concat(Regex.Split(title, @"[^A-Za-z0-9]+").Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        if (method.Length == 0 || char.IsDigit(method[0])) method = "Solve" + method;
        return $"public {ret} {method}({string.Join(", ", ps)})";
    }

    private static string DefaultName(string type, int index) => (type, index) switch
    {
        ("int[]", 0) => "nums",
        ("string", 0) => "s",
        ("int", 0) => "n",
        ("int", _) => "k",
        ("int[][]", 0) => "grid",
        _ => "arg" + index,
    };

    private static string? TypeOf(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt32(out _) => "int",
        JsonValueKind.Number when e.TryGetInt64(out _) => "long",
        JsonValueKind.Number => "double",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.String => "string",
        JsonValueKind.Array when e.GetArrayLength() == 0 => "int[]",
        JsonValueKind.Array => e.EnumerateArray().Select(TypeOf).Distinct().ToList() is [var t] && t is "int" or "long" or "double" or "bool" or "string" or "int[]"
            ? (t == "int[]" ? "int[][]" : t + "[]")
            : null,
        _ => null,
    };

    /// <summary>Splits on commas that are not inside brackets, braces or quotes.</summary>
    private static List<string> TopLevelSplit(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (c is '[' or '{' or '(') depth++;
            else if (c is ']' or '}' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(text[start..].Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    public static string? NormalizeDifficulty(string text) => text.Trim().ToLowerInvariant() switch
    {
        "easy" or "beginner" or "basic" => "Easy",
        "medium" or "intermediate" or "moderate" => "Medium",
        "hard" or "advanced" or "difficult" => "Hard",
        _ => null,
    };

    private static (string Title, string? Difficulty) SplitDifficulty(string title)
    {
        var m = Regex.Match(title, @"\s*[\(\[\-–—|]\s*(Easy|Medium|Hard|Beginner|Intermediate|Advanced)\s*[\)\]]?\s*$", RegexOptions.IgnoreCase);
        return m.Success ? (title[..m.Index].Trim(), NormalizeDifficulty(m.Groups[1].Value)) : (title.Trim(), null);
    }

    private static IEnumerable<string> SplitList(string text) =>
        text.Split([',', ';', '•', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.Trim('-', '*', ' ')).Where(x => x.Length > 0);

    /// <summary>Removes Markdown emphasis, heading marks, bullets and inline code ticks.</summary>
    private static string Clean(string line)
    {
        string s = line.Trim();
        s = Regex.Replace(s, @"^#{1,6}\s*", "");
        s = Regex.Replace(s, @"^[-*•]\s+", "");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return s.Trim();
    }

    private static string StripFence(string text)
    {
        var m = Regex.Match(text, @"^```[A-Za-z]*\s*\n(?<body>[\s\S]*?)\n```\s*$");
        return m.Success ? m.Groups["body"].Value.Trim() : text;
    }

    [GeneratedRegex(@"^\s*(?:#{1,6}\s*)?(?:\*\*|__)?\s*(?:Level|Problem|Exercise|Question|Challenge|Task|Activity|Drill|Kata)\s*#?\s*(?<num>\d+)\s*(?:\*\*|__)?\s*[:.)\-–—]\s*(?:\*\*|__)?\s*(?<title>[^\n]+?)\s*(?:\*\*|__)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"^\s*(?:[-*•]\s*)?(?:#{1,6}\s*)?(?:\*\*|__)?\s*(?<label>[A-Za-z][A-Za-z ]{1,24}?)\s*(?:\d+)?\s*(?:\*\*|__)?\s*[:：]\s*(?:\*\*|__)?(?<value>.*)$")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"^\s*(?:[-*•]\s*)?(?:\*\*)?Input(?:\*\*)?\s*:\s*(?<in>.+?)\s*(?:→|->|=>|,?\s*(?:\*\*)?Output(?:\*\*)?\s*:)\s*(?:(?:\*\*)?Output(?:\*\*)?\s*:)?\s*(?<out>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InlineIoRegex();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.+)$", RegexOptions.Singleline)]
    private static partial Regex NamedArgRegex();
}
