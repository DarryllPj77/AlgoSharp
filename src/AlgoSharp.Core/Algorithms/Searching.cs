using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

public static class Searching
{
    // <code id="linear-search">
    public static int LinearSearch(int[] a, int target, ArrayTracer T)
    {
        T.At("").V("target", target).Say($"Look for {target} by checking every value from left to right.");
        for (int i = 0; i < a.Length; i++) // #loop
        {
            T.At("cmp").Hl(Compare, i).Ptr("i", i).V("i", i).V("a[i]", a[i]).V("target", target).Say(a[i] == target ? $"a[{i}] = {a[i]} equals {target}!" : $"a[{i}] = {a[i]} is not {target}. Move on.");
            if (a[i] == target) // #cmp
            {
                T.Mark(Found, i);
                T.At("found").Ptr("i", i).V("i", i).V("target", target).Say($"Found {target} at index {i}. Return {i}.");
                return i; // #found
            }
            T.Mark(Visited, i);
        }
        T.At("notfound").V("target", target).Say($"Checked all {a.Length} values without a match. Return -1.");
        return -1; // #notfound
    }
    // </code>

    // <code id="binary-search">
    public static int BinarySearch(int[] a, int target, ArrayTracer T)
    {
        int lo = 0, hi = a.Length - 1; // #init
        T.Range(lo, hi);
        T.At("init").Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("hi", hi).V("target", target).Say($"The array is sorted, so the answer must lie in a[{lo}..{hi}]. Search for {target}.");
        while (lo <= hi) // #loop
        {
            int mid = lo + (hi - lo) / 2; // #mid
            T.At("mid").Hl(Compare, mid).Ptr("lo", lo).Ptr("mid", mid).Ptr("hi", hi).V("lo", lo).V("hi", hi).V("mid", mid).V("a[mid]", a[mid]).V("target", target).Say($"Look at the middle: mid = {lo} + ({hi} - {lo}) / 2 = {mid}, a[mid] = {a[mid]}.");
            if (a[mid] == target) // #eq
            {
                T.Mark(Found, mid);
                T.At("found").Ptr("mid", mid).V("mid", mid).V("target", target).Say($"a[{mid}] equals {target}. Found it — return {mid}.");
                return mid; // #found
            }
            if (a[mid] < target) // #lt
            {
                lo = mid + 1; // #right
                T.Range(lo, hi);
                T.At("right").Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("hi", hi).V("mid", mid).V("target", target).Say($"{a[mid]} < {target}, so the target can only be to the right. Discard the left half: lo = {lo}.");
            }
            else
            {
                hi = mid - 1; // #left
                T.Range(lo, hi);
                T.At("left").Ptr("lo", lo).Ptr("hi", hi).V("lo", lo).V("hi", hi).V("mid", mid).V("target", target).Say($"{a[mid]} > {target}, so the target can only be to the left. Discard the right half: hi = {hi}.");
            }
        }
        T.At("notfound").V("lo", lo).V("hi", hi).V("target", target).Say($"lo ({lo}) passed hi ({hi}): the range is empty, so {target} is not in the array. Return -1.");
        return -1; // #notfound
    }
    // </code>
}
