using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoSharp.Tracing;

/// <summary>What a trace event represents.</summary>
public enum TraceEventKind
{
    /// <summary>A statement ran on <see cref="TraceEvent.Line"/>.</summary>
    Line,
    /// <summary>A condition (if / loop) was evaluated; see <see cref="TraceEvent.Result"/>.</summary>
    Condition,
    /// <summary>A method was entered.</summary>
    Enter,
    /// <summary>A return statement produced a value.</summary>
    Return,
    /// <summary>An exception escaped the user's code.</summary>
    Exception,
}

/// <summary>A captured runtime value. Containers keep a stable <see cref="Ref"/> so aliases can be recognised.</summary>
public sealed record TraceValue(
    string Kind,
    string? Text = null,
    IReadOnlyList<string>? Items = null,
    IReadOnlyList<TraceEntry>? Entries = null,
    int Ref = 0,
    string? Type = null,
    bool Truncated = false);

public sealed record TraceEntry(string Key, string Value);

public sealed record TraceVar(string Name, TraceValue Value);

/// <summary>An element read or a key lookup that happened while a statement ran.</summary>
public sealed record TraceAccess(int Ref, string Kind, string Key, string? IndexName, bool? Hit);

public sealed record TraceEvent(
    int Seq,
    TraceEventKind Kind,
    int Line,
    string Method,
    int Depth,
    string? Text,
    bool? Result,
    IReadOnlyList<TraceVar> Vars,
    IReadOnlyList<TraceAccess> Accesses);

public sealed record TestCaseRequest(string Label, IReadOnlyList<JsonElement> Args);

/// <summary>Sent to the sandboxed runner on stdin.</summary>
public sealed record RunRequest(
    string AssemblyBase64,
    string TypeName,
    string MethodName,
    IReadOnlyList<TestCaseRequest> Cases,
    int MaxEvents,
    int MaxOutputChars);

public sealed record CaseResult(
    string Label,
    bool Completed,
    string? ResultJson,
    string? ErrorType,
    string? ErrorMessage,
    int? ErrorLine,
    bool LimitReached,
    string Output,
    IReadOnlyList<TraceEvent> Events);

/// <summary>Written by the runner on stdout.</summary>
public sealed record RunResponse(bool Ok, string? Error, IReadOnlyList<CaseResult> Cases);

public static class TraceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32,
    };
}
