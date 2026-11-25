using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Catalog;

internal static class SessionHelpers
{
    public static int Arg(this IReadOnlyDictionary<string, int> args, string name) =>
        args.TryGetValue(name, out int v) ? v : 0;

    /// <summary>Runs an operation, turning a thrown exception into a failed result with its recorded steps.</summary>
    public static OperationResult Execute(Func<List<Step>> steps, Func<string> run)
    {
        try
        {
            string summary = run();
            return new OperationResult(steps(), summary, false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IndexOutOfRangeException)
        {
            return new OperationResult(steps(), $"{ex.GetType().Name}: {ex.Message}", true);
        }
    }
}

public sealed class SortSession(int[] input, Action<int[], ArrayTracer> sort) : IVisualizerSession
{
    public int[] Values { get; } = input;

    public Step Snapshot(string message) => new ArrayTracer(ArrayLayout.Bars, Values.Length, Values).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        int[] a = (int[])Values.Clone();
        var tracer = new ArrayTracer(ArrayLayout.Bars, a.Length, a);
        sort(a, tracer);
        int comparisons = tracer.Steps.Count(s => s.Label == "cmp");
        int moves = tracer.Steps.Count(s => s.Label is "swap" or "shift" or "takeL" or "takeR" or "restL" or "restR" or "place" or "insert");
        return new OperationResult(tracer.Steps, $"Sorted [{string.Join(", ", a)}] — {comparisons} comparisons, {moves} writes/swaps.", false);
    }
}

public sealed class SearchSession(int[] input, Func<int[], int, ArrayTracer, int> search) : IVisualizerSession
{
    public int[] Values { get; } = input;

    public Step Snapshot(string message) => new ArrayTracer(ArrayLayout.Cells, Values.Length, Values).Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        var tracer = new ArrayTracer(ArrayLayout.Cells, Values.Length, Values);
        int target = args.Arg("target");
        int index = search((int[])Values.Clone(), target, tracer);
        int checks = tracer.Steps.Count(s => s.Label is "cmp" or "mid");
        return new OperationResult(tracer.Steps, index >= 0 ? $"Found {target} at index {index} after {checks} checks." : $"{target} not found after {checks} checks.", false);
    }
}

public sealed class ArraySession(int[] input, int capacity) : IVisualizerSession
{
    private readonly DynamicArray array = new(capacity, input);

    public int[] Values => array.ToArray();

    public Step Snapshot(string message) => array.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        array.T.BeginOperation();
        return SessionHelpers.Execute(() => array.T.Steps, () => operation switch
        {
            "access" => $"Get({args.Arg("index")}) returned {array.Get(args.Arg("index"))}.",
            "insert" => Do(() => array.Insert(args.Arg("index"), args.Arg("value")), $"Inserted {args.Arg("value")} at index {args.Arg("index")}."),
            "delete" => $"RemoveAt({args.Arg("index")}) removed {array.RemoveAt(args.Arg("index"))}.",
            "update" => Do(() => array.Set(args.Arg("index"), args.Arg("value")), $"Set index {args.Arg("index")} to {args.Arg("value")}."),
            "search" => array.IndexOf(args.Arg("value")) is var i and >= 0 ? $"IndexOf({args.Arg("value")}) = {i}." : $"{args.Arg("value")} not found (-1).",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private static string Do(Action action, string summary)
    {
        action();
        return summary;
    }
}

public sealed class StackSession(int[] input, int capacity) : IVisualizerSession
{
    private readonly ArrayStack stack = new(capacity, input);

    public int[] Values => stack.ToArray();

    public Step Snapshot(string message) => stack.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        stack.T.BeginOperation();
        return SessionHelpers.Execute(() => stack.T.Steps, () => operation switch
        {
            "push" => Push(args.Arg("value")),
            "pop" => $"Pop() returned {stack.Pop()}.",
            "peek" => $"Peek() returned {stack.Peek()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Push(int value)
    {
        stack.Push(value);
        return $"Pushed {value}. Size = {stack.Count}.";
    }
}

public sealed class QueueSession(int[] input, int capacity) : IVisualizerSession
{
    private readonly ArrayQueue queue = new(capacity, input);

    public int[] Values => queue.ToArray();

    public Step Snapshot(string message) => queue.T.At("").Ptr("front", queue.Front).Ptr("rear", queue.Rear).Say(message, record: false);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        queue.T.BeginOperation();
        return SessionHelpers.Execute(() => queue.T.Steps, () => operation switch
        {
            "enqueue" => Enqueue(args.Arg("value")),
            "dequeue" => $"Dequeue() returned {queue.Dequeue()}.",
            "peek" => $"Peek() returned {queue.Peek()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Enqueue(int value)
    {
        queue.Enqueue(value);
        return $"Enqueued {value}. Size = {queue.Count}.";
    }
}

public sealed class LinkedListSession(int[] input) : IVisualizerSession
{
    private readonly SinglyLinkedList list = new(input);

    public int[] Values => list.ToArray();

    public Step Snapshot(string message) => list.T.Snapshot(message);

    public OperationResult Run(string operation, IReadOnlyDictionary<string, int> args)
    {
        list.T.BeginOperation();
        return SessionHelpers.Execute(() => list.T.Steps, () => operation switch
        {
            "insert" => Insert(args.Arg("index"), args.Arg("value")),
            "delete" => $"RemoveAt({args.Arg("index")}) removed {list.RemoveAt(args.Arg("index"))}.",
            "search" => list.IndexOf(args.Arg("value")) is var i and >= 0 ? $"IndexOf({args.Arg("value")}) = {i}." : $"{args.Arg("value")} not found (-1).",
            "traverse" => $"Sum() = {list.Sum()}.",
            _ => throw new ArgumentException($"Unknown operation {operation}"),
        });
    }

    private string Insert(int index, int value)
    {
        list.InsertAt(index, value);
        return $"Inserted {value} at position {index}.";
    }
}
