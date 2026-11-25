using AlgoSharp.Core.Algorithms;
using AlgoSharp.Core.Catalog;
using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Tests;

public class SortingTests
{
    public static TheoryData<string> SortSlugs => new() { "bubble-sort", "selection-sort", "insertion-sort", "merge-sort", "quick-sort" };

    [Theory]
    [MemberData(nameof(SortSlugs))]
    public void Sorts_random_arrays_and_visual_model_matches(string slug)
    {
        var def = AlgorithmCatalog.Find(slug)!;
        var rng = new Random(slug.Length * 31);
        for (int trial = 0; trial < 60; trial++)
        {
            int[] input = Enumerable.Range(0, rng.Next(1, 17)).Select(_ => rng.Next(1, 20)).ToArray();
            var session = def.CreateSession!(input);
            var result = session.Run("sort", new Dictionary<string, int>());

            var last = result.Steps[^1].Array!;
            var shown = last.Cells.Where(c => c.Row == 0 && c.State != CellState.Removed).OrderBy(c => c.Pos).Select(c => c.Value).ToArray();
            Assert.Equal(input.Order().ToArray(), shown);
            Assert.All(last.Cells, c => Assert.Equal(CellState.Sorted, c.State));
            // Identity is preserved: the multiset of (id, value) pairs never changes.
            var ids = result.Steps[0].Array!.Cells.Select(c => (c.Id, c.Value)).OrderBy(x => x.Id);
            Assert.All(result.Steps, s => Assert.Equal(ids, s.Array!.Cells.Select(c => (c.Id, c.Value)).OrderBy(x => x.Id)));
            // No two main-row cells ever share a slot.
            Assert.All(result.Steps, s => Assert.Equal(
                s.Array!.Cells.Count(c => c.Row == 0),
                s.Array.Cells.Where(c => c.Row == 0).Select(c => c.Pos).Distinct().Count()));
        }
    }

    [Fact]
    public void Bubble_sort_stops_early_on_sorted_input()
    {
        var session = AlgorithmCatalog.Find("bubble-sort")!.CreateSession!([1, 2, 3, 4, 5]);
        var result = session.Run("sort", new Dictionary<string, int>());
        Assert.Contains(result.Steps, s => s.Label == "early");
        Assert.Equal(4, result.Steps.Count(s => s.Label == "cmp"));
    }
}

public class SearchingTests
{
    [Theory]
    [InlineData(new[] { 3, 9, 14, 21, 27 }, 21, 3)]
    [InlineData(new[] { 3, 9, 14, 21, 27 }, 3, 0)]
    [InlineData(new[] { 3, 9, 14, 21, 27 }, 27, 4)]
    [InlineData(new[] { 3, 9, 14, 21, 27 }, 10, -1)]
    [InlineData(new[] { 5 }, 5, 0)]
    public void Binary_and_linear_search_agree(int[] data, int target, int expected)
    {
        Assert.Equal(expected, Searching.BinarySearch(data, target, new ArrayTracer(ArrayLayout.Cells, data.Length, data)));
        Assert.Equal(expected, Searching.LinearSearch(data, target, new ArrayTracer(ArrayLayout.Cells, data.Length, data)));
    }
}

public class StructureTests
{
    [Fact]
    public void Dynamic_array_matches_list_and_visual_model()
    {
        var rng = new Random(7);
        var reference = new List<int> { 1, 2, 3 };
        var array = new DynamicArray(12, reference);
        for (int i = 0; i < 400; i++)
        {
            array.T.BeginOperation();
            int op = rng.Next(4);
            int v = rng.Next(100);
            if (op == 0 && reference.Count < 12)
            {
                int idx = rng.Next(reference.Count + 1);
                array.Insert(idx, v);
                reference.Insert(idx, v);
            }
            else if (op == 1 && reference.Count > 0)
            {
                int idx = rng.Next(reference.Count);
                Assert.Equal(reference[idx], array.RemoveAt(idx));
                reference.RemoveAt(idx);
            }
            else if (op == 2 && reference.Count > 0)
            {
                int idx = rng.Next(reference.Count);
                array.Set(idx, v);
                reference[idx] = v;
            }
            else
            {
                Assert.Equal(reference.IndexOf(v), array.IndexOf(v));
            }
            Assert.Equal(reference, array.ToArray());
            var slots = array.T.Slots();
            Assert.Equal(reference, slots.Take(reference.Count).Select(s => s!.Value));
            Assert.All(slots.Skip(reference.Count), s => Assert.Null(s));
        }
    }

    [Fact]
    public void Dynamic_array_rejects_bad_index_with_error_step()
    {
        var session = new ArraySession([1, 2, 3], 12);
        var result = session.Run("access", new Dictionary<string, int> { ["index"] = 5 });
        Assert.True(result.Failed);
        Assert.True(result.Steps[^1].IsError);
        Assert.Equal([1, 2, 3], session.Values);
    }

    [Fact]
    public void Linked_list_matches_list()
    {
        var rng = new Random(11);
        var reference = new List<int> { 5, 6 };
        var list = new SinglyLinkedList(reference);
        for (int i = 0; i < 300; i++)
        {
            list.T.BeginOperation();
            int op = rng.Next(4);
            int v = rng.Next(50);
            if (op == 0)
            {
                int idx = rng.Next(reference.Count + 1);
                list.InsertAt(idx, v);
                reference.Insert(idx, v);
            }
            else if (op == 1 && reference.Count > 0)
            {
                int idx = rng.Next(reference.Count);
                Assert.Equal(reference[idx], list.RemoveAt(idx));
                reference.RemoveAt(idx);
            }
            else if (op == 2)
            {
                Assert.Equal(reference.IndexOf(v), list.IndexOf(v));
            }
            else
            {
                Assert.Equal(reference.Sum(), list.Sum());
            }
            Assert.Equal(reference, list.ToArray());
            var frame = list.T.Steps.LastOrDefault()?.List;
            if (frame is not null)
            {
                Assert.Equal(reference, frame.Nodes.Where(n => n.Row == 0).OrderBy(n => n.Pos).Select(n => n.Value));
            }
        }
    }

    [Fact]
    public void Stack_and_queue_match_bcl()
    {
        var rng = new Random(3);
        var stack = new ArrayStack(8, []);
        var queue = new ArrayQueue(8, []);
        var refStack = new Stack<int>();
        var refQueue = new Queue<int>();
        for (int i = 0; i < 500; i++)
        {
            int v = rng.Next(100);
            if (rng.Next(2) == 0 && refStack.Count < 8) { stack.Push(v); refStack.Push(v); }
            else if (refStack.Count > 0) Assert.Equal(refStack.Pop(), stack.Pop());

            if (rng.Next(2) == 0 && refQueue.Count < 8) { queue.Enqueue(v); refQueue.Enqueue(v); }
            else if (refQueue.Count > 0) Assert.Equal(refQueue.Dequeue(), queue.Dequeue());

            Assert.Equal(refStack.Reverse(), stack.ToArray());
            Assert.Equal(refQueue, queue.ToArray());
            var slots = queue.T.Slots();
            for (int k = 0; k < queue.Count; k++) Assert.Equal(queue.ToArray()[k], slots[(queue.Front + k) % 8]);
        }
        Assert.Throws<InvalidOperationException>(() => new ArrayStack(2, []).Pop());
        Assert.Throws<InvalidOperationException>(() => new ArrayQueue(1, [1]).Enqueue(2));
    }
}

public class CodeSyncTests
{
    [Fact]
    public void Every_recorded_step_points_at_a_displayed_line()
    {
        var args = new Dictionary<string, int> { ["index"] = 1, ["value"] = 30, ["target"] = 33, ["start"] = 0, ["n"] = 4 };
        foreach (var def in AlgorithmCatalog.All.Where(a => a.Available))
        {
            foreach (var op in def.Operations)
            {
                var region = CodeLibrary.Get(op.CodeId);
                var session = def.CreateFromText is { } fromText ? fromText(def.Input.DefaultText) : def.CreateSession!(def.Input.Default);
                var result = session.Run(op.Key, args);
                Assert.NotEmpty(result.Steps);
                foreach (var step in result.Steps.Where(s => s.Label != ""))
                {
                    Assert.True(region.LineOf(step.Label) > 0, $"{def.Slug}/{op.Key}: label '{step.Label}' has no line in region '{op.CodeId}'");
                }
            }
        }
    }

    [Fact]
    public void Displayed_code_hides_instrumentation_only()
    {
        foreach (string id in CodeLibrary.RegionIds)
        {
            var region = CodeLibrary.Get(id);
            Assert.DoesNotContain(region.Lines, l => l.TrimStart().StartsWith("T.", StringComparison.Ordinal));
            Assert.DoesNotContain(region.Lines, l => l.Contains("Tracer") || l.Contains("// #"));
            Assert.DoesNotContain(region.Lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\bT\.|\bT\)"));
        }
        var bubble = CodeLibrary.Get("bubble-sort");
        Assert.Equal("public static void BubbleSort(int[] a)", bubble.Lines[0]);
        Assert.Contains("(a[j], a[j + 1]) = (a[j + 1], a[j]);", bubble.Lines[bubble.LineOf("swap") - 1]);
        var merge = CodeLibrary.Get("merge-sort");
        Assert.Contains("MergeSort(a, 0, a.Length - 1);", merge.Lines[merge.LineOf("start") - 1]);
    }
}
