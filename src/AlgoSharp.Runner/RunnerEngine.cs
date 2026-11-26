using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlgoSharp.Tracing;

namespace AlgoSharp.Runner;

/// <summary>
/// Loads an instrumented assembly and runs it against test cases, collecting the trace.
/// This runs ONLY inside the sandbox container (see sandbox/Dockerfile); the web app never calls it.
/// </summary>
public static class RunnerEngine
{
    public static RunResponse Execute(RunRequest request)
    {
        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(request.AssemblyBase64);
        }
        catch (FormatException)
        {
            return new RunResponse(false, "Invalid assembly payload.", []);
        }

        var context = new AssemblyLoadContext("user-solution", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(bytes));
            var type = assembly.GetType(request.TypeName);
            if (type is null) return new RunResponse(false, $"Type {request.TypeName} not found.", []);
            MethodInfo? method = null;
            if (request.DesignMethod is null)
            {
                method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == request.MethodName);
                if (method is null) return new RunResponse(false, $"Method {request.TypeName}.{request.MethodName} not found.", []);
            }

            var results = new List<CaseResult>();
            foreach (var testCase in request.Cases.Take(10))
            {
                results.Add(request.DesignMethod is null
                    ? RunCase(type, method!, testCase, request)
                    : RunDesign(type, request.DesignMethod, testCase, request));
            }
            return new RunResponse(true, null, results);
        }
        finally
        {
            context.Unload();
        }
    }

    private static CaseResult RunCase(Type type, MethodInfo method, TestCaseRequest testCase, RunRequest request)
    {
        object?[] args;
        try
        {
            args = method.GetParameters().Select((p, i) => i < testCase.Args.Count ? Convert(testCase.Args[i], p.ParameterType) : null).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            return new CaseResult(testCase.Label, false, null, "InputError", $"Couldn't convert the test input: {ex.Message}", null, false, "", []);
        }

        return Traced(testCase, request, () =>
        {
            object? instance = method.IsStatic ? null : Activator.CreateInstance(type, nonPublic: true);
            object? result = method.Invoke(instance, args);
            return ToJson(result, method.ReturnType);
        });
    }

    /// <summary>new T(args[0..^1]); then T.Method(x) for each x in args[^1]; result = [return values].</summary>
    private static CaseResult RunDesign(Type type, string methodName, TestCaseRequest testCase, RunRequest request)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        var ctor = type.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == testCase.Args.Count - 1);
        if (method is null || ctor is null)
        {
            return new CaseResult(testCase.Label, false, null, "InputError",
                $"Expected a public constructor with {testCase.Args.Count - 1} parameter(s) and a public method {methodName}.", null, false, "", []);
        }
        object?[] ctorArgs;
        List<object?> calls;
        try
        {
            ctorArgs = ctor.GetParameters().Select((p, i) => Convert(testCase.Args[i], p.ParameterType)).ToArray();
            var paramType = method.GetParameters()[0].ParameterType;
            calls = testCase.Args[^1].EnumerateArray().Select(e => Convert(e, paramType)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException or IndexOutOfRangeException)
        {
            return new CaseResult(testCase.Label, false, null, "InputError", $"Couldn't convert the test input: {ex.Message}", null, false, "", []);
        }

        return Traced(testCase, request, () =>
        {
            object instance = ctor.Invoke(ctorArgs);
            var results = new JsonArray();
            foreach (var call in calls)
            {
                var value = method.Invoke(instance, [call]);
                results.Add(JsonNode.Parse(ToJson(value, method.ReturnType)));
            }
            return results.ToJsonString();
        });
    }

    private static CaseResult Traced(TestCaseRequest testCase, RunRequest request, Func<string> body)
    {
        var output = new BoundedWriter(request.MaxOutputChars);
        var originalOut = Console.Out;
        Console.SetOut(output);
        Tr.Reset(request.MaxEvents);
        try
        {
            string json = body();
            return new CaseResult(testCase.Label, true, json, null, null, null, false, output.Text, Tr.Finish());
        }
        catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
        {
            Tr.RecordException(inner);
            var events = Tr.Finish();
            return new CaseResult(testCase.Label, false, null, inner.GetType().Name, inner.Message, Tr.CurrentLine, inner is TraceLimitException, output.Text, events);
        }
        catch (Exception ex) when (ex is MissingMethodException or MemberAccessException or ArgumentException or NotSupportedException)
        {
            return new CaseResult(testCase.Label, false, null, ex.GetType().Name, ex.Message, null, false, output.Text, Tr.Finish());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // ------------------------------------------------------------------ input conversion

    /// <summary>JSON → parameter. ListNode/TreeNode (LeetCode style) are built from arrays.</summary>
    private static object? Convert(JsonElement json, Type type)
    {
        if (IsNode(type, "next")) return BuildList(json, type);
        if (IsNode(type, "left")) return BuildTree(json, type);
        return json.Deserialize(type, TraceJson.Options);
    }

    private static bool IsNode(Type type, string field) =>
        type.IsClass && type.GetField(field, BindingFlags.Public | BindingFlags.Instance)?.FieldType == type;

    private static object NewNode(Type type, int value)
    {
        // LeetCode's node classes only have constructors with optional parameters.
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(c => c.GetParameters().Length).FirstOrDefault()
                   ?? throw new InvalidOperationException($"Cannot create {type.Name}.");
        var node = ctor.Invoke(ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray());
        (type.GetField("val") ?? type.GetField("value") ?? throw new InvalidOperationException($"{type.Name} needs a public int val field."))
            .SetValue(node, value);
        return node;
    }

    /// <summary>[1,2,3] or {"values":[1,2,3],"pos":1} (tail links back to index pos, for cycle problems).</summary>
    private static object? BuildList(JsonElement json, Type type)
    {
        int pos = -1;
        if (json.ValueKind == JsonValueKind.Object)
        {
            pos = json.TryGetProperty("pos", out var p) ? p.GetInt32() : -1;
            json = json.GetProperty("values");
        }
        if (json.ValueKind == JsonValueKind.Null) return null;
        var next = type.GetField("next")!;
        var nodes = json.EnumerateArray().Select(e => NewNode(type, e.GetInt32())).ToList();
        for (int i = 0; i + 1 < nodes.Count; i++) next.SetValue(nodes[i], nodes[i + 1]);
        if (pos >= 0 && pos < nodes.Count && nodes.Count > 0) next.SetValue(nodes[^1], nodes[pos]);
        return nodes.Count == 0 ? null : nodes[0];
    }

    /// <summary>Level-order array with nulls, e.g. [3,9,20,null,null,15,7].</summary>
    private static object? BuildTree(JsonElement json, Type type)
    {
        if (json.ValueKind == JsonValueKind.Null) return null;
        var values = json.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? (int?)null : e.GetInt32()).ToList();
        if (values.Count == 0 || values[0] is null) return null;
        var left = type.GetField("left")!;
        var right = type.GetField("right")!;
        var root = NewNode(type, values[0]!.Value);
        var queue = new Queue<object>([root]);
        int i = 1;
        while (queue.Count > 0 && i < values.Count)
        {
            var node = queue.Dequeue();
            if (i < values.Count && values[i] is int l) { var child = NewNode(type, l); left.SetValue(node, child); queue.Enqueue(child); }
            i++;
            if (i < values.Count && values[i] is int r) { var child = NewNode(type, r); right.SetValue(node, child); queue.Enqueue(child); }
            i++;
        }
        return root;
    }

    // ------------------------------------------------------------------ output conversion

    private static string ToJson(object? value, Type declared)
    {
        if (value is not null && IsNode(value.GetType(), "next")) return ListToJson(value);
        if (value is not null && IsNode(value.GetType(), "left")) return TreeToJson(value);
        if (value is null && (IsNode(declared, "next") || IsNode(declared, "left"))) return "[]";
        return JsonSerializer.Serialize(value, declared == typeof(void) ? typeof(object) : declared, TraceJson.Options);
    }

    private static int ValueOf(object node) =>
        (int)(node.GetType().GetField("val") ?? node.GetType().GetField("value"))!.GetValue(node)!;

    private static string ListToJson(object head)
    {
        var next = head.GetType().GetField("next")!;
        var values = new List<int>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (object? n = head; n is not null && values.Count < 10_000; n = next.GetValue(n))
        {
            if (!seen.Add(n)) return JsonSerializer.Serialize(new { values, cycle = true });
            values.Add(ValueOf(n));
        }
        return JsonSerializer.Serialize(values);
    }

    private static string TreeToJson(object root)
    {
        var left = root.GetType().GetField("left")!;
        var right = root.GetType().GetField("right")!;
        var values = new List<int?>();
        var queue = new Queue<object?>([root]);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (queue.Count > 0 && values.Count < 10_000)
        {
            var n = queue.Dequeue();
            if (n is null || !seen.Add(n))
            {
                values.Add(null);
                continue;
            }
            values.Add(ValueOf(n));
            queue.Enqueue(left.GetValue(n));
            queue.Enqueue(right.GetValue(n));
        }
        while (values.Count > 0 && values[^1] is null) values.RemoveAt(values.Count - 1);
        return JsonSerializer.Serialize(values);
    }

    /// <summary>Console output capped at a fixed size; extra output is dropped with a marker.</summary>
    private sealed class BoundedWriter(int max) : TextWriter
    {
        private readonly StringBuilder sb = new();
        private bool truncated;

        public override Encoding Encoding => Encoding.UTF8;
        public string Text => truncated ? sb + "\n… output truncated" : sb.ToString();

        public override void Write(char value)
        {
            if (sb.Length < max) sb.Append(value);
            else truncated = true;
        }
    }
}
