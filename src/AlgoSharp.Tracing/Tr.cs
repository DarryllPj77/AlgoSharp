using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AlgoSharp.Tracing;

/// <summary>Thrown when the event budget is exhausted (likely an infinite loop).</summary>
public sealed class TraceLimitException(string message) : Exception(message);

/// <summary>
/// Runtime hooks called by instrumented user code. Each statement opens a pending event;
/// reads and lookups that happen while it runs attach to it, and the variable snapshot is
/// taken when the next event starts, so an event shows the state <em>after</em> its line ran.
/// </summary>
public static class Tr
{
    private sealed class Frame(string method)
    {
        public readonly string Method = method;
        public readonly Dictionary<string, object?> Vars = [];
        public readonly List<string> Order = [];
    }

    private sealed class Pending
    {
        public TraceEventKind Kind;
        public int Line;
        public string? Text;
        public bool? Result;
        public Frame Frame = null!;
        public int Depth;
        public readonly List<TraceAccess> Accesses = [];
    }

    private const int MaxItems = 64;

    private static readonly List<TraceEvent> events = [];
    private static readonly Stack<Frame> frames = new();
    private static readonly ConditionalWeakTable<object, object> refs = [];
    private static Pending? pending;
    private static int nextRef = 1;
    private static int maxEvents = 2000;
    private static long hardLimit = 4000;
    private static long calls;

    /// <summary>True once the budget is exhausted; instrumented catch clauses let the abort through.</summary>
    public static bool Aborting { get; private set; }

    public static int CurrentLine { get; private set; }

    public static void Reset(int eventBudget)
    {
        events.Clear();
        frames.Clear();
        pending = null;
        maxEvents = eventBudget;
        hardLimit = (long)eventBudget * 50;
        calls = 0;
        Aborting = false;
        CurrentLine = 0;
        fault = null;
        faultEvent = null;
        frames.Push(new Frame("<entry>"));
    }

    /// <summary>Closes the last pending event and returns the trace.</summary>
    public static List<TraceEvent> Finish()
    {
        Flush();
        return [.. events];
    }

    /// <summary>Records the exception that ended execution, with the variables at that point.</summary>
    public static void RecordException(Exception ex)
    {
        Flush();
        if (ReferenceEquals(fault, ex) && faultEvent is not null)
        {
            events.Add(faultEvent with { Seq = events.Count });
            return;
        }
        var frame = frames.Count > 0 ? frames.Peek() : new Frame("<entry>");
        events.Add(new TraceEvent(events.Count, TraceEventKind.Exception, CurrentLine, frame.Method, Math.Max(0, frames.Count - 1),
            $"{ex.GetType().Name}: {ex.Message}", null, Snapshot(frame), []));
    }

    private static Exception? fault;
    private static TraceEvent? faultEvent;

    /// <summary>
    /// Called from an exception filter in every instrumented method. Filters run before the stack
    /// unwinds, innermost frame first, so this captures the variables where the exception happened.
    /// Always returns false: the exception keeps propagating unchanged.
    /// </summary>
    public static bool Fault(Exception ex)
    {
        if (ReferenceEquals(fault, ex)) return false;
        Flush();
        fault = ex;
        var frame = frames.Peek();
        faultEvent = new TraceEvent(0, TraceEventKind.Exception, CurrentLine, frame.Method, Math.Max(0, frames.Count - 1),
            $"{ex.GetType().Name}: {ex.Message}", null, Snapshot(frame), []);
        return false;
    }

    // ---------------------------------------------------------------- hooks

    public static void Step(int line) => Open(TraceEventKind.Line, line, null, null);

    public static bool Cond(bool value, int line, string text)
    {
        Open(TraceEventKind.Condition, line, text, value);
        return value;
    }

    /// <summary>Records variables, returns true (used inside loop conditions).</summary>
    public static bool Vars(params (string Name, object? Value)[] vars)
    {
        // Loop variables change in the incrementor, which belongs to the next condition, not the last body line.
        Flush();
        foreach (var (name, value) in vars) Set(name, value);
        return true;
    }

    public static TValue Set<TValue>(string name, TValue value)
    {
        var frame = frames.Peek();
        if (!frame.Vars.ContainsKey(name)) frame.Order.Add(name);
        frame.Vars[name] = value;
        return value;
    }

    public static void Enter(string method, int line, params (string Name, object? Value)[] args)
    {
        Flush();
        var frame = new Frame(method);
        frames.Push(frame);
        foreach (var (name, value) in args)
        {
            frame.Order.Add(name);
            frame.Vars[name] = value;
        }
        Open(TraceEventKind.Enter, line, method, null);
    }

    public static void Exit()
    {
        Flush();
        if (frames.Count > 1) frames.Pop();
    }

    public static TValue Ret<TValue>(TValue value, int line)
    {
        Open(TraceEventKind.Return, line, Format(Describe(value)), null);
        return value;
    }

    public static TElement Read<TElement>(TElement[] array, int index, string arrayName, string? indexName)
    {
        TElement value = array[index];
        Access(array, "read", index.ToString(CultureInfo.InvariantCulture), indexName, null);
        return value;
    }

    public static TElement Read<TElement>(List<TElement> list, int index, string listName, string? indexName)
    {
        TElement value = list[index];
        Access(list, "read", index.ToString(CultureInfo.InvariantCulture), indexName, null);
        return value;
    }

    public static TValue ReadKey<TKey, TValue>(Dictionary<TKey, TValue> map, TKey key, string mapName) where TKey : notnull
    {
        Access(map, "read", Text(key), null, map.ContainsKey(key));
        return map[key];
    }

    /// <summary>Records a key lookup on a dictionary or set, then passes the key through.</summary>
    public static TKey Key<TKey>(object container, TKey key)
    {
        bool? hit = container switch
        {
            IDictionary d when key is not null => d.Contains(key),
            _ => ContainsViaInterface(container, key),
        };
        Access(container, "probe", Text(key), null, hit);
        return key;
    }

    // ---------------------------------------------------------------- internals

    private static bool? ContainsViaInterface<TKey>(object container, TKey key) => container switch
    {
        ICollection<TKey> c => c.Contains(key),
        _ => null,
    };

    private static void Access(object container, string kind, string key, string? indexName, bool? hit)
    {
        Budget();
        if (pending is null) Open(TraceEventKind.Line, CurrentLine, null, null);
        if (pending!.Accesses.Count < 32) pending.Accesses.Add(new TraceAccess(RefOf(container), kind, key, indexName, hit));
    }

    private static void Open(TraceEventKind kind, int line, string? text, bool? result)
    {
        Budget();
        Flush();
        CurrentLine = line;
        if (events.Count >= maxEvents)
        {
            Aborting = true;
            throw new TraceLimitException($"Stopped after {maxEvents} steps. The code may be in an infinite loop, or the input is too large to visualize.");
        }
        pending = new Pending { Kind = kind, Line = line, Text = text, Result = result, Frame = frames.Peek(), Depth = frames.Count - 1 };
    }

    private static void Budget()
    {
        if (++calls > hardLimit || Aborting)
        {
            Aborting = true;
            throw new TraceLimitException("Execution limit reached.");
        }
    }

    private static void Flush()
    {
        if (pending is null) return;
        var p = pending;
        pending = null;
        events.Add(new TraceEvent(events.Count, p.Kind, p.Line, p.Frame.Method, p.Depth, p.Text, p.Result, Snapshot(p.Frame), [.. p.Accesses]));
    }

    /// <summary>Human-readable form of a captured value, e.g. [0, 1] or {2: 0}.</summary>
    public static string Format(TraceValue v) => v.Kind switch
    {
        "array" or "list" or "set" or "stack" or "queue" => "[" + string.Join(", ", v.Items ?? []) + (v.Truncated ? ", …" : "") + "]",
        "dict" => "{" + string.Join(", ", (v.Entries ?? []).Select(e => $"{e.Key}: {e.Value}")) + (v.Truncated ? ", …" : "") + "}",
        _ => v.Text ?? v.Kind,
    };

    private static List<TraceVar> Snapshot(Frame frame) =>
        frame.Order.Select(name => new TraceVar(name, Describe(frame.Vars[name]))).ToList();

    private static int RefOf(object o)
    {
        if (refs.TryGetValue(o, out var id)) return (int)id;
        int next = nextRef++;
        refs.Add(o, next);
        return next;
    }

    private static string Text(object? value) => value switch
    {
        null => "null",
        string s => "\"" + (s.Length > 40 ? s[..40] + "…" : s) + "\"",
        char c => "'" + c + "'",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name,
    };

    /// <summary>Converts a runtime value to a serializable description without calling user code.</summary>
    public static TraceValue Describe(object? value)
    {
        switch (value)
        {
            case null:
                return new TraceValue("null", "null");
            case string or char or bool or Enum:
            case IFormattable when value.GetType().IsPrimitive || value is decimal:
                return new TraceValue("scalar", Text(value), Type: value.GetType().Name);
            case Array arr when arr.Rank == 1:
                return Sequence("array", arr, value, $"{CSharpName(arr.GetType().GetElementType()!)}[]");
            case IDictionary dict:
            {
                var entries = new List<TraceEntry>();
                foreach (DictionaryEntry e in dict)
                {
                    if (entries.Count >= MaxItems) break;
                    entries.Add(new TraceEntry(Text(e.Key), Text(e.Value)));
                }
                return new TraceValue("dict", null, Entries: entries, Ref: RefOf(value), Type: TypeName(value), Truncated: dict.Count > MaxItems);
            }
            case IEnumerable seq when IsKnownCollection(value):
            {
                string kind = TypeBase(value) switch
                {
                    "HashSet" or "SortedSet" => "set",
                    "Stack" => "stack",
                    "Queue" => "queue",
                    _ => "list",
                };
                return Sequence(kind, seq, value, TypeName(value));
            }
            default:
                return new TraceValue("object", value.GetType().Name, Ref: RefOf(value), Type: value.GetType().Name);
        }
    }

    private static TraceValue Sequence(string kind, IEnumerable seq, object owner, string type)
    {
        var items = new List<string>();
        bool truncated = false;
        foreach (var item in seq)
        {
            if (items.Count >= MaxItems)
            {
                truncated = true;
                break;
            }
            items.Add(Text(item));
        }
        return new TraceValue(kind, null, items, Ref: RefOf(owner), Type: type, Truncated: truncated);
    }

    private static bool IsKnownCollection(object value) =>
        value.GetType().Namespace == "System.Collections.Generic" &&
        TypeBase(value) is "List" or "HashSet" or "SortedSet" or "Stack" or "Queue" or "LinkedList";

    private static string TypeBase(object value)
    {
        string n = value.GetType().Name;
        int tick = n.IndexOf('`');
        return tick < 0 ? n : n[..tick];
    }

    private static string TypeName(object value)
    {
        var t = value.GetType();
        if (!t.IsGenericType) return t.Name;
        return TypeBase(value) + "<" + string.Join(", ", t.GetGenericArguments().Select(CSharpName)) + ">";
    }

    private static string CSharpName(Type t) => t switch
    {
        _ when t == typeof(int) => "int",
        _ when t == typeof(long) => "long",
        _ when t == typeof(bool) => "bool",
        _ when t == typeof(string) => "string",
        _ when t == typeof(char) => "char",
        _ when t == typeof(double) => "double",
        _ when t.IsArray => CSharpName(t.GetElementType()!) + "[]",
        _ => t.Name,
    };
}
