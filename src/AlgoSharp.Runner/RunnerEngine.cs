using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
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
            bytes = Convert.FromBase64String(request.AssemblyBase64);
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
            var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == request.MethodName);
            if (type is null || method is null) return new RunResponse(false, $"Entry point {request.TypeName}.{request.MethodName} not found.", []);

            var results = new List<CaseResult>();
            foreach (var testCase in request.Cases.Take(8))
            {
                results.Add(RunCase(type, method, testCase, request));
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
        var parameters = method.GetParameters();
        object?[] args;
        try
        {
            args = parameters.Select((p, i) => i < testCase.Args.Count ? testCase.Args[i].Deserialize(p.ParameterType, TraceJson.Options) : null).ToArray();
        }
        catch (JsonException ex)
        {
            return new CaseResult(testCase.Label, false, null, "InputError", $"Couldn't convert the test input: {ex.Message}", null, false, "", []);
        }

        var output = new BoundedWriter(request.MaxOutputChars);
        var originalOut = Console.Out;
        Console.SetOut(output);
        Tr.Reset(request.MaxEvents);
        try
        {
            object? instance = method.IsStatic ? null : Activator.CreateInstance(type, nonPublic: true);
            object? result = method.Invoke(instance, args);
            var events = Tr.Finish();
            string json = JsonSerializer.Serialize(result, method.ReturnType, TraceJson.Options);
            return new CaseResult(testCase.Label, true, json, null, null, null, false, output.Text, events);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is { } inner)
        {
            Tr.RecordException(inner);
            var events = Tr.Finish();
            bool limit = inner is TraceLimitException;
            return new CaseResult(testCase.Label, false, null, inner.GetType().Name, inner.Message, Tr.CurrentLine, limit, output.Text, events);
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
