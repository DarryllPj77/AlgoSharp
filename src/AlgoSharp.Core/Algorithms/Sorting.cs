using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>
/// Sorting algorithms. Everything between <c>// &lt;code id&gt;</c> markers is shown in the UI.
/// Lines starting with <c>T.</c> only record visual steps and are hidden from the displayed code.
/// </summary>
public static class Sorting
{
    // <code id="bubble-sort">
    public static void BubbleSort(int[] a, ArrayTracer T)
    {
        int n = a.Length; // #n
        T.At("n").V("n", n).Say($"The array holds {n} values. Each pass carries the largest unsorted value to the end, like a bubble rising.");
        for (int i = 0; i < n - 1; i++) // #outer
        {
            T.At("outer").Ptr("end", n - 1 - i).V("n", n).V("i", i).Say($"Pass {i + 1}: walk through positions 0 to {n - 1 - i}, comparing neighbours.");
            bool swapped = false; // #flag
            for (int j = 0; j < n - 1 - i; j++) // #inner
            {
                T.At("cmp").Hl(Compare, j, j + 1).Ptr("j", j).Ptr("j+1", j + 1).V("i", i).V("j", j).V("a[j]", a[j]).V("a[j+1]", a[j + 1]).V("swapped", swapped).Say(a[j] > a[j + 1] ? $"Compare {a[j]} and {a[j + 1]}: {a[j]} is bigger, so they are out of order." : $"Compare {a[j]} and {a[j + 1]}: already in order, nothing to do.");
                if (a[j] > a[j + 1]) // #cmp
                {
                    (a[j], a[j + 1]) = (a[j + 1], a[j]); // #swap
                    T.Swap(j, j + 1);
                    swapped = true; // #mark
                    T.At("swap").Hl(Swap, j, j + 1).Ptr("j", j).Ptr("j+1", j + 1).V("i", i).V("j", j).V("a[j]", a[j]).V("a[j+1]", a[j + 1]).V("swapped", swapped).Say($"Swap them using a tuple: {a[j + 1]} moves right, {a[j]} moves left.");
                }
            }
            T.Mark(Sorted, n - 1 - i);
            T.At("outer").V("i", i).V("swapped", swapped).Say($"End of pass {i + 1}: {a[n - 1 - i]} has bubbled into its final position.");
            if (!swapped) // #early
            {
                T.MarkAll(Sorted);
                T.At("early").V("i", i).V("swapped", swapped).Say("No swaps happened in this pass, so the array is already sorted. Stop early.");
                break; // #break
            }
        }
        T.MarkAll(Sorted);
        T.At("").V("n", n).Say("Done! Every value is in sorted order.");
    }
    // </code>

    // <code id="selection-sort">
    public static void SelectionSort(int[] a, ArrayTracer T)
    {
        int n = a.Length; // #n
        for (int i = 0; i < n - 1; i++) // #outer
        {
            int min = i; // #min
            T.At("min").Hl(Min, i).Ptr("i", i).Ptr("min", min).V("i", i).V("min", min).V("a[min]", a[min]).Say($"Pass {i + 1}: assume the smallest remaining value is a[{i}] = {a[i]}.");
            for (int j = i + 1; j < n; j++) // #inner
            {
                T.At("cmp").Hl(Min, min).Hl(Compare, j).Ptr("i", i).Ptr("min", min).Ptr("j", j).V("i", i).V("j", j).V("min", min).V("a[j]", a[j]).V("a[min]", a[min]).Say(a[j] < a[min] ? $"{a[j]} < {a[min]}: found a new smallest value." : $"{a[j]} ≥ {a[min]}: the minimum stays {a[min]}.");
                if (a[j] < a[min]) // #cmp
                {
                    min = j; // #newmin
                    T.At("newmin").Hl(Min, min).Ptr("i", i).Ptr("min", min).V("i", i).V("j", j).V("min", min).V("a[min]", a[min]).Say($"Remember index {min} as the position of the minimum.");
                }
            }
            (a[i], a[min]) = (a[min], a[i]); // #swap
            T.Swap(i, min);
            T.Mark(Sorted, i);
            T.At("swap").Hl(Swap, i, min).Ptr("i", i).Ptr("min", min).V("i", i).V("min", min).Say(min == i ? $"{a[i]} is already the smallest, so it stays at index {i}." : $"Swap the minimum {a[i]} into index {i}. Positions 0..{i} are now sorted.");
        }
        T.MarkAll(Sorted);
        T.At("").V("n", n).Say("Done! The last remaining value is automatically in place.");
    }
    // </code>

    // <code id="insertion-sort">
    public static void InsertionSort(int[] a, ArrayTracer T)
    {
        T.Mark(Sorted, 0);
        T.At("").Say("The first value on its own is a sorted prefix of length 1.");
        for (int i = 1; i < a.Length; i++) // #outer
        {
            int key = a[i]; // #key
            T.Lift(i);
            T.At("key").HlAux(Key, i).PtrAux("key", i).V("i", i).V("key", key).Say($"Pick up key = {key}. We will insert it into the sorted part on its left.");
            int j = i - 1; // #j
            while (j >= 0 && a[j] > key) // #cmp
            {
                T.At("cmp").Hl(Compare, j).HlAux(Key, j + 1).Ptr("j", j).V("i", i).V("j", j).V("key", key).V("a[j]", a[j]).Say($"{a[j]} > {key}, so {a[j]} must move one place to the right.");
                a[j + 1] = a[j]; // #shift
                T.Move(j, j + 1);
                T.MoveAux(j + 1, j);
                T.At("shift").Hl(Swap, j + 1).HlAux(Key, j).Ptr("j", j).V("i", i).V("j", j).V("key", key).Say($"Shift {a[j + 1]} right to index {j + 1}, opening a gap at index {j}.");
                j--; // #dec
            }
            T.At("cmp").Hl(Compare, j).HlAux(Key, j + 1).Ptr("j", j).V("i", i).V("j", j).V("key", key).Say(j >= 0 ? $"{a[j]} ≤ {key}: the gap at index {j + 1} is where the key belongs." : $"Reached the start of the array: the key belongs at index 0.");
            a[j + 1] = key; // #insert
            T.Drop(j + 1, j + 1);
            T.MarkRange(Sorted, 0, i);
            T.At("insert").Hl(Key, j + 1).Ptr("j+1", j + 1).V("i", i).V("j", j).V("key", key).Say($"Insert {key} at index {j + 1}. Positions 0..{i} are now sorted.");
        }
        T.MarkAll(Sorted);
        T.At("").Say("Done! Every key has been inserted into its place.");
    }
    // </code>

    // <code id="merge-sort">
    public static void MergeSort(int[] a, ArrayTracer T)
    {
        MergeSort(a, 0, a.Length - 1, T); // #start
        T.ClearRange();
        T.MarkAll(Sorted);
        T.At("").Say("Done! The whole array has been merged back together in order.");
    }

    private static void MergeSort(int[] a, int lo, int hi, ArrayTracer T)
    {
        if (lo >= hi) // #base
        {
            T.Range(lo, hi);
            T.At("base").Hl(Active, lo).Ptr("lo", lo).V("lo", lo).V("hi", hi).Say(lo == hi ? $"A single value (a[{lo}] = {a[lo]}) is already sorted." : "Empty range: nothing to sort.");
            return; // #return
        }
        int mid = (lo + hi) / 2; // #mid
        T.Range(lo, hi);
        T.At("mid").Ptr("lo", lo).Ptr("mid", mid).Ptr("hi", hi).V("lo", lo).V("mid", mid).V("hi", hi).Say($"Split a[{lo}..{hi}] into a[{lo}..{mid}] and a[{mid + 1}..{hi}].");
        MergeSort(a, lo, mid, T); // #left
        MergeSort(a, mid + 1, hi, T); // #right
        Merge(a, lo, mid, hi, T); // #merge
    }

    private static void Merge(int[] a, int lo, int mid, int hi, ArrayTracer T)
    {
        int[] left = a[lo..(mid + 1)]; // #copyL
        int[] right = a[(mid + 1)..(hi + 1)]; // #copyR
        T.Range(lo, hi);
        T.LiftRange(lo, hi);
        T.At("copyR").Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("mid", mid).V("hi", hi).V("left", left).V("right", right).Say($"Copy both sorted halves into temporary arrays: left = [{string.Join(", ", left)}], right = [{string.Join(", ", right)}].");
        int i = 0, j = 0, k = lo; // #init
        while (i < left.Length && j < right.Length) // #while
        {
            T.At("cmp").HlAux(Compare, lo + i, mid + 1 + j).Ptr("k", k).V("i", i).V("j", j).V("k", k).V("left[i]", left[i]).V("right[j]", right[j]).Say(left[i] <= right[j] ? $"Compare left[{i}] = {left[i]} with right[{j}] = {right[j]}: the left one is smaller (or equal)." : $"Compare left[{i}] = {left[i]} with right[{j}] = {right[j]}: the right one is smaller.");
            if (left[i] <= right[j]) // #cmp
            {
                T.Drop(lo + i, k);
                T.At("takeL").Hl(New, k).Ptr("k", k).V("i", i + 1).V("j", j).V("k", k + 1).Say($"Write {left[i]} into a[{k}] and advance i and k.");
                a[k++] = left[i++]; // #takeL
            }
            else
            {
                T.Drop(mid + 1 + j, k);
                T.At("takeR").Hl(New, k).Ptr("k", k).V("i", i).V("j", j + 1).V("k", k + 1).Say($"Write {right[j]} into a[{k}] and advance j and k.");
                a[k++] = right[j++]; // #takeR
            }
        }
        while (i < left.Length) // #restL
        {
            T.Drop(lo + i, k);
            T.At("restL").Hl(New, k).Ptr("k", k).V("i", i + 1).V("k", k + 1).Say($"The right half is used up. Copy the leftover {left[i]} into a[{k}].");
            a[k++] = left[i++]; // #copyRestL
        }
        while (j < right.Length) // #restR
        {
            T.Drop(mid + 1 + j, k);
            T.At("restR").Hl(New, k).Ptr("k", k).V("j", j + 1).V("k", k + 1).Say($"The left half is used up. Copy the leftover {right[j]} into a[{k}].");
            a[k++] = right[j++]; // #copyRestR
        }
        T.At("merge").Hl(Active, Enumerable.Range(lo, hi - lo + 1).ToArray()).V("lo", lo).V("hi", hi).Say($"a[{lo}..{hi}] is merged and sorted: [{string.Join(", ", a[lo..(hi + 1)])}].");
    }
    // </code>

    // <code id="quick-sort">
    public static void QuickSort(int[] a, ArrayTracer T)
    {
        QuickSort(a, 0, a.Length - 1, T); // #start
        T.ClearRange();
        T.MarkAll(Sorted);
        T.At("").Say("Done! Every value has been placed by a partition step.");
    }

    private static void QuickSort(int[] a, int lo, int hi, ArrayTracer T)
    {
        if (lo >= hi) // #base
        {
            T.MarkRange(Sorted, lo, hi);
            T.Range(lo, hi);
            T.At("base").Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("hi", hi).Say(lo == hi ? $"A single value (a[{lo}] = {a[lo]}) is already in place." : "Empty range: nothing to sort.");
            return; // #return
        }
        int p = Partition(a, lo, hi, T); // #partition
        QuickSort(a, lo, p - 1, T); // #left
        QuickSort(a, p + 1, hi, T); // #right
    }

    private static int Partition(int[] a, int lo, int hi, ArrayTracer T)
    {
        int pivot = a[hi]; // #pivot
        T.Range(lo, hi);
        T.At("pivot").Hl(Pivot, hi).Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("hi", hi).V("pivot", pivot).Say($"Partition a[{lo}..{hi}] around the last value, pivot = {pivot}.");
        int i = lo - 1; // #i
        for (int j = lo; j < hi; j++) // #loop
        {
            T.At("cmp").Hl(Pivot, hi).Hl(Compare, j).Ptr("i", i).Ptr("j", j).V("i", i).V("j", j).V("pivot", pivot).V("a[j]", a[j]).Say(a[j] <= pivot ? $"{a[j]} ≤ {pivot}: it belongs on the left side." : $"{a[j]} > {pivot}: leave it on the right side.");
            if (a[j] <= pivot) // #cmp
            {
                i++; // #inc
                (a[i], a[j]) = (a[j], a[i]); // #swap
                T.Swap(i, j);
                T.At("swap").Hl(Pivot, hi).Hl(Swap, i, j).Ptr("i", i).Ptr("j", j).V("i", i).V("j", j).V("pivot", pivot).Say(i == j ? $"Grow the left side: i = {i} (a[{i}] is already there)." : $"Grow the left side to i = {i} and swap {a[i]} into it.");
            }
        }
        (a[i + 1], a[hi]) = (a[hi], a[i + 1]); // #place
        T.Swap(i + 1, hi);
        T.Mark(Sorted, i + 1);
        T.At("place").Hl(Pivot, i + 1).Ptr("p", i + 1).V("i", i).V("pivot", pivot).Say($"Move the pivot {pivot} to index {i + 1}. Everything left of it is ≤ {pivot}, everything right is greater.");
        return i + 1; // #ret
    }
    // </code>
}
