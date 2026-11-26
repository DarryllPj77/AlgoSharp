using System.Text.Json;
using System.Text.Json.Nodes;
using static AlgoSharp.Playground.ProblemInputs;

namespace AlgoSharp.Playground;

/// <summary>
/// Trusted reference answers, computed on the server for any input (examples and custom cases).
/// This is AlgoSharp's own code; user code never runs here.
/// </summary>
internal static class References
{
    public static JsonNode V(bool b) => JsonValue.Create(b);
    public static JsonNode V(int n) => JsonValue.Create(n);
    public static JsonNode V(uint n) => JsonValue.Create(n);
    public static JsonNode V(IEnumerable<int> xs) => new JsonArray([.. xs.Select(x => (JsonNode)JsonValue.Create(x))]);

    public static string S(JsonElement e) => e.GetString()!;

    // ------------------------------------------------------------------ trees

    public sealed class Node(int val)
    {
        public int Val = val;
        public Node? Left, Right;
    }

    public static Node? Tree(JsonElement e)
    {
        var v = NullableInts(e);
        if (v.Length == 0 || v[0] is null) return null;
        var root = new Node(v[0]!.Value);
        var q = new Queue<Node>([root]);
        int i = 1;
        while (q.Count > 0 && i < v.Length)
        {
            var n = q.Dequeue();
            if (i < v.Length && v[i] is int l) q.Enqueue(n.Left = new Node(l));
            i++;
            if (i < v.Length && v[i] is int r) q.Enqueue(n.Right = new Node(r));
            i++;
        }
        return root;
    }

    public static JsonNode TreeJson(Node? root)
    {
        var values = new List<int?>();
        var q = new Queue<Node?>([root]);
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            values.Add(n?.Val);
            if (n is null) continue;
            q.Enqueue(n.Left);
            q.Enqueue(n.Right);
        }
        while (values.Count > 0 && values[^1] is null) values.RemoveAt(values.Count - 1);
        return new JsonArray([.. values.Select(x => x is null ? null : (JsonNode)JsonValue.Create(x.Value))]);
    }

    public static int Height(Node? n) => n is null ? 0 : 1 + Math.Max(Height(n.Left), Height(n.Right));

    public static bool Same(Node? a, Node? b) => a is null || b is null ? a == b : a.Val == b.Val && Same(a.Left, b.Left) && Same(a.Right, b.Right);

    public static Node? Invert(Node? n)
    {
        if (n is null) return null;
        (n.Left, n.Right) = (Invert(n.Right), Invert(n.Left));
        return n;
    }

    public static int Diameter(Node? root)
    {
        int best = 0;
        int Depth(Node? n)
        {
            if (n is null) return 0;
            int l = Depth(n.Left), r = Depth(n.Right);
            best = Math.Max(best, l + r);
            return 1 + Math.Max(l, r);
        }
        Depth(root);
        return best;
    }

    public static bool Balanced(Node? n) => n is null || Math.Abs(Height(n.Left) - Height(n.Right)) <= 1 && Balanced(n.Left) && Balanced(n.Right);

    public static bool Subtree(Node? root, Node? sub) => Same(root, sub) || root is not null && (Subtree(root.Left, sub) || Subtree(root.Right, sub));

    // ------------------------------------------------------------------ misc

    public static bool Anagram(string s, string t) => s.Length == t.Length && s.Order().SequenceEqual(t.Order());

    public static bool Palindrome(string s)
    {
        var c = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return c.SequenceEqual(c.Reverse());
    }

    public static bool Parentheses(string s)
    {
        var st = new Stack<char>();
        foreach (char ch in s)
        {
            if (ch is '(' or '[' or '{') st.Push(ch);
            else if (st.Count == 0 || st.Pop() != ch switch { ')' => '(', ']' => '[', '}' => '{', _ => '?' }) return false;
        }
        return st.Count == 0;
    }

    public static int MaxProfit(int[] p)
    {
        int best = 0, min = int.MaxValue;
        foreach (int x in p) { min = Math.Min(min, x); best = Math.Max(best, x - min); }
        return best;
    }

    public static int[] KthLargest(int k, int[] nums, int[] adds)
    {
        var all = new List<int>(nums);
        var result = new List<int>();
        foreach (int a in adds)
        {
            all.Add(a);
            result.Add(all.OrderByDescending(x => x).ElementAt(k - 1));
        }
        return [.. result];
    }

    public static int LastStone(int[] stones)
    {
        var s = new List<int>(stones);
        while (s.Count > 1)
        {
            s.Sort();
            int y = s[^1], x = s[^2];
            s.RemoveRange(s.Count - 2, 2);
            if (y != x) s.Add(y - x);
        }
        return s.Count == 0 ? 0 : s[0];
    }

    public static int ClimbStairs(int n)
    {
        int a = 1, b = 1;
        for (int i = 2; i <= n; i++) (a, b) = (b, a + b);
        return b;
    }

    public static int MinCost(int[] cost)
    {
        int a = 0, b = 0;
        for (int i = 2; i <= cost.Length; i++) (a, b) = (b, Math.Min(b + cost[i - 1], a + cost[i - 2]));
        return b;
    }

    public static bool CanAttend(JsonElement intervals)
    {
        var v = intervals.EnumerateArray().Select(Ints).OrderBy(x => x[0]).ToList();
        for (int i = 1; i < v.Count; i++) if (v[i][0] < v[i - 1][1]) return false;
        return true;
    }

    public static bool Happy(int n)
    {
        var seen = new HashSet<int>();
        while (n != 1 && seen.Add(n)) n = n.ToString().Sum(c => (c - '0') * (c - '0'));
        return n == 1;
    }

    public static int[] PlusOne(int[] d)
    {
        var digits = new List<int>(d);
        for (int i = digits.Count - 1; i >= 0; i--)
        {
            if (digits[i] < 9) { digits[i]++; return [.. digits]; }
            digits[i] = 0;
        }
        digits.Insert(0, 1);
        return [.. digits];
    }

    public static uint ReverseBits(uint n)
    {
        uint r = 0;
        for (int i = 0; i < 32; i++) { r = (r << 1) | (n & 1); n >>= 1; }
        return r;
    }
}
