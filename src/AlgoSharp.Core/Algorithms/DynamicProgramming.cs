using System.Text;
using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Bottom-up dynamic programming. Each table cell is computed once from cells already filled.</summary>
public static class DynamicProgramming
{
    public static TableTracer LcsTable(string a, string b) =>
        new(["ε", .. a.Select(c => c.ToString())], ["ε", .. b.Select(c => c.ToString())], "a \\ b");

    // <code id="lcs">
    public static string Lcs(string a, string b, TableTracer T)
    {
        int[,] dp = new int[a.Length + 1, b.Length + 1]; // #alloc
        T.Read((r, c) => dp[r, c].ToString());
        T.FillRow(0);
        T.FillCol(0);
        T.At("alloc").V("a", a).V("b", b).Say($"dp[i, j] = length of the LCS of the first i letters of \"{a}\" and the first j letters of \"{b}\". Row 0 and column 0 compare with an empty string, so they are 0.");
        for (int i = 1; i <= a.Length; i++) // #outer
        {
            for (int j = 1; j <= b.Length; j++) // #inner
            {
                if (a[i - 1] == b[j - 1]) // #match
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1; // #diag
                    T.Fill(i, j);
                    T.At("diag").Hl(Compare, i - 1, j - 1).Hl(New, i, j).V("i", i).V("j", j).V("a[i-1]", a[i - 1]).V("b[j-1]", b[j - 1]).V("dp[i, j]", dp[i, j]).Say($"'{a[i - 1]}' = '{b[j - 1]}': extend the diagonal answer. dp[{i}, {j}] = {dp[i - 1, j - 1]} + 1 = {dp[i, j]}.");
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]); // #max
                    T.Fill(i, j);
                    T.At("max").Hl(Compare, i - 1, j).Hl(Compare, i, j - 1).Hl(New, i, j).V("i", i).V("j", j).V("a[i-1]", a[i - 1]).V("b[j-1]", b[j - 1]).V("dp[i, j]", dp[i, j]).Say($"'{a[i - 1]}' ≠ '{b[j - 1]}': drop a letter from one string and keep the better result: max({dp[i - 1, j]}, {dp[i, j - 1]}) = {dp[i, j]}.");
                }
            }
        }
        var lcs = new StringBuilder(); // #sb
        int x = a.Length, y = b.Length; // #start
        T.Mark(Key, x, y);
        T.At("start").Hl(Key, x, y).V("length", dp[x, y]).Say($"The bottom-right cell holds the answer's length: {dp[x, y]}. Walk back from it to recover the letters.");
        while (x > 0 && y > 0) // #trace
        {
            if (a[x - 1] == b[y - 1]) // #same
            {
                lcs.Insert(0, a[x - 1]); // #take
                T.Mark(Found, x, y);
                T.At("take").Hl(Found, x, y).V("x", x).V("y", y).V("lcs", lcs.ToString()).Say($"'{a[x - 1]}' matches: it is part of the LCS. Move diagonally.");
                x--; // #diagX
                y--; // #diagY
            }
            else if (dp[x - 1, y] >= dp[x, y - 1]) // #up
            {
                T.Mark(Visited, x, y);
                T.At("goUp").Hl(Visited, x, y).V("x", x).V("y", y).V("lcs", lcs.ToString()).Say($"No match; the value came from above ({dp[x - 1, y]}), so move up.");
                x--; // #goUp
            }
            else
            {
                T.Mark(Visited, x, y);
                T.At("goLeft").Hl(Visited, x, y).V("x", x).V("y", y).V("lcs", lcs.ToString()).Say($"No match; the value came from the left ({dp[x, y - 1]}), so move left.");
                y--; // #goLeft
            }
        }
        T.At("ret").V("lcs", lcs.ToString()).V("length", lcs.Length).Say($"LCS = \"{lcs}\" (length {lcs.Length}). The table has (m+1)(n+1) cells, each O(1): O(m·n) time and space.");
        return lcs.ToString(); // #ret
    }
    // </code>

    /// <summary>Two rows: the minimum number of coins for each amount, and the last coin used.</summary>
    public static TableTracer CoinTable(int amount) =>
        new(["min coins", "last coin"], [.. Enumerable.Range(0, amount + 1).Select(i => i.ToString())], "amount");

    // <code id="coin-change">
    public static int MinCoins(int[] coins, int amount, TableTracer T)
    {
        int[] dp = new int[amount + 1]; // #alloc
        int[] last = new int[amount + 1]; // #allocLast
        T.Read((r, c) => r == 0 ? (dp[c] == int.MaxValue ? "∞" : dp[c].ToString()) : (last[c] == 0 ? "–" : last[c].ToString()));
        Array.Fill(dp, int.MaxValue); // #fill
        dp[0] = 0; // #base
        T.FillAll();
        T.At("base").Hl(New, 0, 0).V("coins", coins).V("amount", amount).Say("dp[a] = fewest coins that sum to a. Start with ∞ everywhere except dp[0] = 0 (no coins needed for 0).");
        for (int a = 1; a <= amount; a++) // #amount
        {
            foreach (int c in coins) // #coin
            {
                T.At("better").Hl(Key, 0, a).Hl(Compare, 0, c <= a ? a - c : -1).V("a", a).V("c", c).V("dp[a]", dp[a] == int.MaxValue ? "∞" : dp[a]).V("dp[a-c]", c <= a ? dp[a - c] == int.MaxValue ? "∞" : dp[a - c] : "–").Say(c > a ? $"Coin {c} is bigger than {a}: skip." : dp[a - c] == int.MaxValue ? $"{a - c} can't be made, so coin {c} doesn't help." : dp[a - c] + 1 < dp[a] ? $"Using coin {c}: dp[{a - c}] + 1 = {dp[a - c] + 1}, better than {(dp[a] == int.MaxValue ? "∞" : dp[a])}." : $"Using coin {c}: dp[{a - c}] + 1 = {dp[a - c] + 1}, not better than {dp[a]}.");
                if (c <= a && dp[a - c] != int.MaxValue && dp[a - c] + 1 < dp[a]) // #better
                {
                    dp[a] = dp[a - c] + 1; // #update
                    last[a] = c; // #last
                    T.At("last").Hl(New, 0, a).Hl(New, 1, a).V("a", a).V("c", c).V("dp[a]", dp[a]).Say($"dp[{a}] = {dp[a]} (last coin {c}).");
                }
            }
            T.Mark(Sorted, 0, a);
        }
        int result = dp[amount] == int.MaxValue ? -1 : dp[amount]; // #result
        T.At("result").Hl(Found, 0, amount).V("result", result).Say(result < 0 ? $"{amount} cannot be made from these coins." : $"Answer: {result} coin(s). Follow 'last coin' back from {amount} to list them: {string.Join(" + ", Path(last, amount))}. O(amount × coins).");
        return result; // #ret
    }
    // </code>

    private static IEnumerable<int> Path(int[] last, int amount)
    {
        for (int a = amount; a > 0 && last[a] > 0; a -= last[a]) yield return last[a];
    }

    public static TableTracer FibTable(int n) =>
        new(["fib(i)"], [.. Enumerable.Range(0, n + 1).Select(i => i.ToString())], "i");

    // <code id="fib-table">
    public static long Fib(int n, TableTracer T)
    {
        long[] dp = new long[n + 1]; // #alloc
        T.Read((_, c) => dp[c].ToString());
        dp[0] = 0; // #base0
        if (n >= 1) dp[1] = 1; // #base1
        T.Fill(0, 0);
        T.Fill(0, Math.Min(1, n));
        T.At("base1").Hl(New, 0, 0).Hl(New, 0, Math.Min(1, n)).V("n", n).Say("Bottom-up: fill the base cases fib(0) = 0 and fib(1) = 1 first.");
        for (int i = 2; i <= n; i++) // #loop
        {
            dp[i] = dp[i - 1] + dp[i - 2]; // #step
            T.Fill(0, i);
            T.At("step").Hl(Compare, 0, i - 1).Hl(Compare, 0, i - 2).Hl(New, 0, i).V("i", i).V("dp[i]", dp[i]).Say($"fib({i}) = {dp[i - 1]} + {dp[i - 2]} = {dp[i]}. Both inputs are already in the table — no recursion.");
        }
        T.At("ret").Hl(Found, 0, n).V("fib(n)", dp[n]).Say($"fib({n}) = {dp[n]} in {Math.Max(0, n - 1)} additions: O(n), compared with O(2ⁿ) for the naive recursion.");
        return dp[n]; // #ret
    }
    // </code>
}
