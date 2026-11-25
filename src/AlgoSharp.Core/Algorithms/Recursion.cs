using AlgoSharp.Core.Engine;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Recursive functions whose call trees are recorded by <see cref="CallTreeTracer"/>.</summary>
public static class Recursion
{
    // <code id="fib">
    public static int Fib(int n, CallTreeTracer T)
    {
        T.Enter($"f({n})");
        T.At("base").V("n", n).V("depth", T.Depth).V("calls", T.Calls).Say(n <= 1 ? $"fib({n}) is a base case." : $"fib({n}) is not a base case: it needs fib({n - 1}) and fib({n - 2}).");
        if (n <= 1) // #base
        {
            T.Return(n);
            T.At("retBase").V("n", n).V("calls", T.Calls).Say($"Return {n}.");
            return n; // #retBase
        }
        int a = Fib(n - 1, T); // #left
        T.At("left").V("n", n).V("a", a).V("calls", T.Calls).Say($"fib({n - 1}) returned {a}. Next, call fib({n - 2}).");
        int b = Fib(n - 2, T); // #right
        T.Return(a + b);
        T.At("ret").V("n", n).V("a", a).V("b", b).V("calls", T.Calls).Say($"fib({n}) = {a} + {b} = {a + b}. Notice how many subtrees repeat: this is O(2ⁿ).");
        return a + b; // #ret
    }
    // </code>

    // <code id="fib-memo">
    public static int FibMemo(int n, CallTreeTracer T)
    {
        return FibMemo(n, new Dictionary<int, int>(), T); // #start
    }

    private static int FibMemo(int n, Dictionary<int, int> memo, CallTreeTracer T)
    {
        T.Enter($"f({n})");
        T.At("memo").V("n", n).V("memo size", memo.Count).V("calls", T.Calls).Say(memo.ContainsKey(n) ? $"fib({n}) is already in the memo table!" : $"fib({n}) is not cached yet.");
        if (memo.TryGetValue(n, out int cached)) // #memo
        {
            T.Return(cached, memo: true);
            T.At("memoHit").V("n", n).V("cached", cached).V("calls", T.Calls).Say($"Return the cached {cached} immediately — no recursion needed.");
            return cached; // #memoHit
        }
        if (n <= 1) // #base
        {
            T.Return(n);
            T.At("retBase").V("n", n).V("calls", T.Calls).Say($"Base case: return {n}.");
            return n; // #retBase
        }
        int result = FibMemo(n - 1, memo, T) + FibMemo(n - 2, memo, T); // #recurse
        memo[n] = result; // #store
        T.Return(result);
        T.At("store").V("n", n).V("result", result).V("memo size", memo.Count).V("calls", T.Calls).Say($"Store memo[{n}] = {result}. Each fib(k) is computed once: O(n).");
        return result; // #ret
    }
    // </code>

    // <code id="factorial">
    public static long Factorial(int n, CallTreeTracer T)
    {
        T.Enter($"f({n})");
        T.At("base").V("n", n).V("depth", T.Depth).Say(n <= 1 ? $"{n}! is a base case." : $"{n}! = {n} × {n - 1}!, so call factorial({n - 1}) first.");
        if (n <= 1) // #base
        {
            T.Return(1);
            T.At("retBase").V("n", n).Say("Return 1. Now the waiting calls can finish, from the deepest up.");
            return 1; // #retBase
        }
        long rest = Factorial(n - 1, T); // #recurse
        T.Return(n * rest);
        T.At("ret").V("n", n).V("rest", rest).V("result", n * rest).Say($"{n}! = {n} × {rest} = {n * rest}.");
        return n * rest; // #ret
    }
    // </code>
}
