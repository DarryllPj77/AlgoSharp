using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>A fixed-capacity array plus a <c>count</c> of used slots — the core of List&lt;T&gt;.</summary>
public sealed class DynamicArray
{
    private readonly int[] data;
    private int count;

    public DynamicArray(int capacity, IEnumerable<int> values)
    {
        data = new int[capacity];
        foreach (int v in values.Take(capacity)) data[count++] = v;
        T = new ArrayTracer(ArrayLayout.Cells, capacity, data[..count]);
    }

    public ArrayTracer T { get; }
    public int Count => count;
    public int Capacity => data.Length;
    public int[] ToArray() => data[..count];

    // <code id="array-access">
    public int Get(int index)
    {
        T.At("check").Ptr("index", index).V("index", index).V("count", count).Say($"Check that index {index} is inside 0..{count - 1}.");
        if (index < 0 || index >= count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error($"Index {index} is outside the used part of the array (0..{count - 1}).");
            throw new IndexOutOfRangeException(); // #throw
        }
        T.Mark(Found, index);
        T.At("read").Ptr("index", index).V("index", index).V("data[index]", data[index]).Say($"Jump straight to slot {index} — the address is computed, no searching needed. Value = {data[index]}. This is O(1).");
        return data[index]; // #read
    }
    // </code>

    // <code id="array-update">
    public void Set(int index, int value)
    {
        T.At("check").Ptr("index", index).V("index", index).V("value", value).V("count", count).Say($"Check that index {index} is inside 0..{count - 1}.");
        if (index < 0 || index >= count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error($"Index {index} is outside the used part of the array (0..{count - 1}).");
            throw new IndexOutOfRangeException(); // #throw
        }
        T.At("write").Hl(Compare, index).Ptr("index", index).V("index", index).V("old", data[index]).V("value", value).Say($"Slot {index} currently holds {data[index]}.");
        data[index] = value; // #write
        T.SetValue(index, value);
        T.At("write").Ptr("index", index).V("index", index).V("data[index]", data[index]).Say($"Overwrite it with {value}. Updating by index is O(1).");
    }
    // </code>

    // <code id="array-insert">
    public void Insert(int index, int value)
    {
        T.At("full").V("count", count).V("capacity", data.Length).Say($"Is there room? count = {count}, capacity = {data.Length}.");
        if (count == data.Length) // #full
        {
            T.At("throwFull").Error("The array is full. A real List<T> would allocate a bigger array and copy everything over.");
            throw new InvalidOperationException("Array is full"); // #throwFull
        }
        if (index < 0 || index > count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error($"Index {index} is invalid: you can insert at 0..{count}.");
            throw new IndexOutOfRangeException(); // #throw
        }
        for (int k = count; k > index; k--) // #loop
        {
            T.At("loop").Hl(Compare, k - 1).Ptr("k", k).Ptr("k-1", k - 1).V("index", index).V("k", k).Say($"Make room: slot {k} takes the value from slot {k - 1}.");
            data[k] = data[k - 1]; // #shift
            T.Move(k - 1, k);
            T.At("shift").Hl(Swap, k).Ptr("k", k).V("index", index).V("k", k).V("data[k]", data[k]).Say($"Shifted {data[k]} one slot to the right.");
        }
        data[index] = value; // #write
        T.Create(index, value);
        T.At("write").Ptr("index", index).V("index", index).V("value", value).Say($"Slot {index} is free now. Write {value} into it.");
        count++; // #count
        T.At("count").V("count", count).Say($"count becomes {count}. Inserting shifted {count - 1 - index} element(s) — O(n) in the worst case.");
    }
    // </code>

    // <code id="array-delete">
    public int RemoveAt(int index)
    {
        if (index < 0 || index >= count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error($"Index {index} is outside the used part of the array (0..{count - 1}).");
            throw new IndexOutOfRangeException(); // #throw
        }
        int removed = data[index]; // #read
        T.At("read").Hl(Key, index).Ptr("index", index).V("index", index).V("removed", removed).Say($"Remember the value {removed} at slot {index}.");
        T.Remove(index);
        T.At("read").Ptr("index", index).V("index", index).V("removed", removed).Say($"Remove it, leaving a gap at slot {index}.");
        for (int k = index; k < count - 1; k++) // #loop
        {
            data[k] = data[k + 1]; // #shift
            T.Move(k + 1, k);
            T.At("shift").Hl(Swap, k).Ptr("k", k).V("index", index).V("k", k).V("data[k]", data[k]).Say($"Close the gap: move {data[k]} from slot {k + 1} to slot {k}.");
        }
        count--; // #count
        T.At("count").V("count", count).V("removed", removed).Say($"count becomes {count}. Return {removed}.");
        return removed; // #return
    }
    // </code>

    // <code id="array-search">
    public int IndexOf(int value)
    {
        for (int i = 0; i < count; i++) // #loop
        {
            T.At("cmp").Hl(Compare, i).Ptr("i", i).V("i", i).V("data[i]", data[i]).V("value", value).Say(data[i] == value ? $"data[{i}] = {data[i]} matches!" : $"data[{i}] = {data[i]} is not {value}.");
            if (data[i] == value) // #cmp
            {
                T.Mark(Found, i);
                T.At("found").Ptr("i", i).V("i", i).Say($"Found {value} at index {i}.");
                return i; // #found
            }
            T.Mark(Visited, i);
        }
        T.At("notfound").V("value", value).Say($"{value} is not in the array. Return -1.");
        return -1; // #notfound
    }
    // </code>
}
