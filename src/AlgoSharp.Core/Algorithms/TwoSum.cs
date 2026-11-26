using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>
/// LeetCode 1, Two Sum: return the indices of two different elements that add up to target,
/// or an empty array when no such pair exists.
/// </summary>
public static class TwoSum
{
    // <code id="two-sum-brute">
    public static int[] BruteForce(int[] nums, int target, ArrayTracer T)
    {
        T.Counter("comparisons");
        T.At("").V("target", target).Say($"Try every pair (i, j) with i < j and check whether it adds up to {target}.");
        for (int i = 0; i < nums.Length; i++) // #outer
        {
            for (int j = i + 1; j < nums.Length; j++) // #inner
            {
                T.Tick("comparisons");
                T.At("check").Hl(Compare, i, j).Ptr("i", i).Ptr("j", j).V("i", i).V("j", j).V("nums[i]", nums[i]).V("nums[j]", nums[j]).V("target", target).Callout($"{nums[i]} + {nums[j]} = {nums[i] + nums[j]} {(nums[i] + nums[j] == target ? "=" : "≠")} {target}", nums[i] + nums[j] == target ? Found : Compare).Say(nums[i] + nums[j] == target ? $"nums[{i}] + nums[{j}] = {target}. Found the pair!" : $"Pair ({i}, {j}) sums to {nums[i] + nums[j]}, not {target}. Try the next pair.");
                if (nums[i] + nums[j] == target) // #check
                {
                    T.Mark(Found, i, j);
                    T.At("found").Ptr("i", i).Ptr("j", j).V("result", new[] { i, j }).Callout($"return [{i}, {j}]", Found).Say($"Return [{i}, {j}] after {T.Count("comparisons")} comparison(s). In the worst case brute force checks all n(n−1)/2 pairs: O(n²).");
                    return [i, j]; // #found
                }
            }
            T.Mark(Visited, i);
        }
        T.At("none").V("target", target).Callout("return []", Error).Say($"All {T.Count("comparisons")} pairs checked: no two numbers add up to {target}.");
        return []; // #none
    }
    // </code>

    // <code id="two-sum-hashmap">
    public static int[] HashMap(int[] nums, int target, ArrayTracer T)
    {
        var seen = new Dictionary<int, int>(); // #map
        T.Watch(seen, "seen: value → index");
        T.Counter("lookups");
        T.Counter("insertions");
        T.At("map").V("target", target).Say("Walk the array once. For each number, ask the dictionary whether its complement (target − number) was seen earlier.");
        for (int i = 0; i < nums.Length; i++) // #loop
        {
            int complement = target - nums[i]; // #complement
            T.Tick("lookups");
            T.At("lookup").Hl(Key, i).Ptr("i", i).Lookup(complement).HlKey(Compare, complement).V("i", i).V("nums[i]", nums[i]).V("complement", complement).V("seen.Count", seen.Count).Callout($"complement = {target} − {nums[i]} = {complement}", Key).Say(seen.ContainsKey(complement) ? $"Is {complement} in the dictionary? Yes — it was at index {seen[complement]}." : $"Is {complement} in the dictionary? No. A hash lookup is O(1) on average, no matter how many entries it holds.");
            if (seen.TryGetValue(complement, out int j)) // #lookup
            {
                T.Mark(Found, j, i);
                T.At("found").Ptr("j", j).Ptr("i", i).Lookup(complement).HlKey(Found, complement).V("j", j).V("i", i).V("result", new[] { j, i }).Callout($"{nums[j]} + {nums[i]} = {target}  →  return [{j}, {i}]", Found).Say($"nums[{j}] + nums[{i}] = {target}. Return [{j}, {i}] after {T.Count("lookups")} lookup(s) and {T.Count("insertions")} insertion(s): one pass, O(n).");
                return [j, i]; // #found
            }
            seen[nums[i]] = i; // #insert
            T.Tick("insertions");
            T.Mark(Visited, i);
            T.At("insert").Ptr("i", i).HlKey(New, nums[i]).V("i", i).V("nums[i]", nums[i]).V("seen.Count", seen.Count).Callout($"seen[{nums[i]}] = {i}", New).Say(Array.IndexOf(nums, nums[i], 0, i) >= 0 ? $"{nums[i]} was already stored; overwrite it with the newer index {i}. Either index works as a partner later." : $"Remember that value {nums[i]} lives at index {i}, so a later number can find it as its complement.");
        }
        T.At("none").V("target", target).Callout("return []", Error).Say($"Reached the end: no number's complement was ever seen. No pair adds up to {target}.");
        return []; // #none
    }
    // </code>
}
