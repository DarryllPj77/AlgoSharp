using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Binary min-heap stored in an array: the children of index i live at 2i+1 and 2i+2.</summary>
public sealed class MinHeap
{
    private readonly int[] items;
    private int count;

    public MinHeap(int capacity, IEnumerable<int> values)
    {
        items = new int[capacity];
        T = new ArrayTracer(ArrayLayout.Cells, capacity);
        foreach (int v in values.Take(capacity)) Insert(v);
        T.BeginOperation();
    }

    public ArrayTracer T { get; }
    public int Count => count;
    public int[] ToArray() => items[..count];

    // <code id="heap-insert">
    public void Insert(int value)
    {
        if (count == items.Length) // #full
        {
            T.At("throw").V("count", count).Error("The heap's array is full.");
            throw new InvalidOperationException("Heap is full"); // #throw
        }
        items[count] = value; // #place
        T.Create(count, value);
        T.At("place").Ptr("i", count).V("value", value).V("count", count).Say($"Put {value} in the first free slot (index {count}), the next leaf of the complete tree.");
        int i = count; // #i
        count++; // #count
        while (i > 0) // #loop
        {
            int parent = (i - 1) / 2; // #parent
            T.At("parent").Hl(Compare, i, parent).Ptr("i", i).Ptr("parent", parent).V("i", i).V("parent", parent).V("items[i]", items[i]).V("items[parent]", items[parent]).Say($"Its parent is at (i - 1) / 2 = {parent}. Compare {items[i]} with parent {items[parent]}.");
            if (items[parent] <= items[i]) // #cmp
            {
                T.At("stop").Hl(Found, i).Ptr("i", i).V("i", i).Say($"{items[parent]} ≤ {items[i]}: the heap property holds. Stop sifting up.");
                break; // #stop
            }
            (items[parent], items[i]) = (items[i], items[parent]); // #swap
            T.Swap(i, parent);
            T.At("swap").Hl(Swap, i, parent).Ptr("i", i).Ptr("parent", parent).V("i", i).V("parent", parent).Say($"{items[parent]} is smaller than its parent: swap them (sift up).");
            i = parent; // #up
        }
        T.At("loop").V("count", count).Say(i == 0 ? $"{items[0]} is at the root: it is the minimum. Insert is O(log n)." : "Insert complete. At most one swap per level: O(log n).");
    }
    // </code>

    // <code id="heap-extract">
    public int ExtractMin()
    {
        if (count == 0) // #empty
        {
            T.At("throw").Error("The heap is empty.");
            throw new InvalidOperationException("Heap is empty"); // #throw
        }
        int min = items[0]; // #min
        T.At("min").Hl(Key, 0).Ptr("root", 0).V("min", min).Say($"The minimum is always at the root: {min}.");
        count--; // #count
        items[0] = items[count]; // #moveLast
        T.Remove(0);
        T.MoveIfPresent(count, 0);
        T.At("moveLast").Hl(Swap, 0).Ptr("i", 0).V("min", min).V("count", count).Say(count > 0 ? $"Remove it and move the last leaf ({items[0]}) to the root, keeping the tree complete." : "That was the only value: the heap is now empty.");
        int i = 0; // #i
        while (true) // #loop
        {
            int left = 2 * i + 1; // #left
            int right = 2 * i + 2; // #right
            int smallest = i; // #smallest
            if (left < count && items[left] < items[smallest]) // #cmpL
            {
                smallest = left; // #takeL
            }
            if (right < count && items[right] < items[smallest]) // #cmpR
            {
                smallest = right; // #takeR
            }
            T.At("cmpR").Hl(Compare, i, left, right).Hl(Min, smallest).Ptr("i", i).Ptr("left", left).Ptr("right", right).V("i", i).V("left", left).V("right", right).V("smallest", smallest).Say(left >= count ? $"Index {i} has no children." : smallest == i ? $"{items[i]} is not larger than its children." : $"The smallest of the three is {items[smallest]} at index {smallest}.");
            if (smallest == i) // #done
            {
                T.At("break").Hl(Found, i).Ptr("i", i).V("i", i).Say(count == 0 ? "Nothing to fix." : $"Heap property restored. ExtractMin is O(log n).");
                break; // #break
            }
            (items[i], items[smallest]) = (items[smallest], items[i]); // #swap
            T.Swap(i, smallest);
            T.At("swap").Hl(Swap, i, smallest).Ptr("i", i).Ptr("smallest", smallest).V("i", i).V("smallest", smallest).Say($"Swap {items[smallest]} down with the smaller child {items[i]} (sift down).");
            i = smallest; // #down
        }
        T.At("ret").V("min", min).Say($"Return {min}.");
        return min; // #ret
    }
    // </code>

    // <code id="heap-peek">
    public int Peek()
    {
        if (count == 0) // #empty
        {
            T.At("throw").Error("The heap is empty.");
            throw new InvalidOperationException("Heap is empty"); // #throw
        }
        T.At("ret").Hl(Found, 0).Ptr("root", 0).V("items[0]", items[0]).Say($"The minimum sits at index 0: {items[0]}. Peek is O(1).");
        return items[0]; // #ret
    }
    // </code>
}
