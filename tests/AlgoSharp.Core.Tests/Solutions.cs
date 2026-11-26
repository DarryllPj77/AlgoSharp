namespace AlgoSharp.Core.Tests;

/// <summary>Correct user-style solutions, used to prove each problem works end to end.</summary>
internal static class Solutions
{
    public static readonly Dictionary<string, string> BySlug = new()
    {
        ["contains-duplicate"] = """
            public class Solution {
                public bool ContainsDuplicate(int[] nums) {
                    var seen = new HashSet<int>();
                    foreach (int x in nums) { if (!seen.Add(x)) return true; }
                    return false;
                }
            }
            """,
        ["valid-anagram"] = """
            public class Solution {
                public bool IsAnagram(string s, string t) {
                    if (s.Length != t.Length) return false;
                    int[] count = new int[26];
                    for (int i = 0; i < s.Length; i++) { count[s[i] - 'a']++; count[t[i] - 'a']--; }
                    foreach (int c in count) if (c != 0) return false;
                    return true;
                }
            }
            """,
        ["two-sum"] = """
            public class Solution {
                public int[] TwoSum(int[] nums, int target) {
                    var seen = new Dictionary<int, int>();
                    for (int i = 0; i < nums.Length; i++) {
                        if (seen.TryGetValue(target - nums[i], out int j)) return new[] { j, i };
                        seen[nums[i]] = i;
                    }
                    return new int[0];
                }
            }
            """,
        ["valid-palindrome"] = """
            public class Solution {
                public bool IsPalindrome(string s) {
                    int l = 0, r = s.Length - 1;
                    while (l < r) {
                        if (!char.IsLetterOrDigit(s[l])) { l++; continue; }
                        if (!char.IsLetterOrDigit(s[r])) { r--; continue; }
                        if (char.ToLower(s[l]) != char.ToLower(s[r])) return false;
                        l++; r--;
                    }
                    return true;
                }
            }
            """,
        ["best-time-to-buy-and-sell-stock"] = """
            public class Solution {
                public int MaxProfit(int[] prices) {
                    int min = int.MaxValue, best = 0;
                    foreach (int p in prices) { min = Math.Min(min, p); best = Math.Max(best, p - min); }
                    return best;
                }
            }
            """,
        ["valid-parentheses"] = """
            public class Solution {
                public bool IsValid(string s) {
                    var stack = new Stack<char>();
                    foreach (char c in s) {
                        if (c == '(' || c == '[' || c == '{') { stack.Push(c); continue; }
                        if (stack.Count == 0) return false;
                        char open = stack.Pop();
                        if (c == ')' && open != '(' || c == ']' && open != '[' || c == '}' && open != '{') return false;
                    }
                    return stack.Count == 0;
                }
            }
            """,
        ["binary-search"] = """
            public class Solution {
                public int Search(int[] nums, int target) {
                    int lo = 0, hi = nums.Length - 1;
                    while (lo <= hi) {
                        int mid = lo + (hi - lo) / 2;
                        if (nums[mid] == target) return mid;
                        if (nums[mid] < target) lo = mid + 1; else hi = mid - 1;
                    }
                    return -1;
                }
            }
            """,
        ["reverse-linked-list"] = """
            public class Solution {
                public ListNode ReverseList(ListNode head) {
                    ListNode prev = null, curr = head;
                    while (curr != null) {
                        ListNode next = curr.next;
                        curr.next = prev;
                        prev = curr;
                        curr = next;
                    }
                    return prev;
                }
            }
            """,
        ["merge-two-sorted-lists"] = """
            public class Solution {
                public ListNode MergeTwoLists(ListNode list1, ListNode list2) {
                    var dummy = new ListNode();
                    var tail = dummy;
                    while (list1 != null && list2 != null) {
                        if (list1.val <= list2.val) { tail.next = list1; list1 = list1.next; }
                        else { tail.next = list2; list2 = list2.next; }
                        tail = tail.next;
                    }
                    tail.next = list1 ?? list2;
                    return dummy.next;
                }
            }
            """,
        ["linked-list-cycle"] = """
            public class Solution {
                public bool HasCycle(ListNode head) {
                    ListNode slow = head, fast = head;
                    while (fast != null && fast.next != null) {
                        slow = slow.next;
                        fast = fast.next.next;
                        if (slow == fast) return true;
                    }
                    return false;
                }
            }
            """,
        ["invert-binary-tree"] = """
            public class Solution {
                public TreeNode InvertTree(TreeNode root) {
                    if (root == null) return null;
                    TreeNode left = InvertTree(root.left);
                    root.left = InvertTree(root.right);
                    root.right = left;
                    return root;
                }
            }
            """,
        ["maximum-depth-of-binary-tree"] = """
            public class Solution {
                public int MaxDepth(TreeNode root) {
                    if (root == null) return 0;
                    return 1 + Math.Max(MaxDepth(root.left), MaxDepth(root.right));
                }
            }
            """,
        ["diameter-of-binary-tree"] = """
            public class Solution {
                private int best;
                public int DiameterOfBinaryTree(TreeNode root) { best = 0; Height(root); return best; }
                private int Height(TreeNode node) {
                    if (node == null) return 0;
                    int l = Height(node.left), r = Height(node.right);
                    best = Math.Max(best, l + r);
                    return 1 + Math.Max(l, r);
                }
            }
            """,
        ["balanced-binary-tree"] = """
            public class Solution {
                public bool IsBalanced(TreeNode root) => Check(root) >= 0;
                private int Check(TreeNode n) {
                    if (n == null) return 0;
                    int l = Check(n.left), r = Check(n.right);
                    if (l < 0 || r < 0 || Math.Abs(l - r) > 1) return -1;
                    return 1 + Math.Max(l, r);
                }
            }
            """,
        ["same-tree"] = """
            public class Solution {
                public bool IsSameTree(TreeNode p, TreeNode q) {
                    if (p == null || q == null) return p == q;
                    return p.val == q.val && IsSameTree(p.left, q.left) && IsSameTree(p.right, q.right);
                }
            }
            """,
        ["subtree-of-another-tree"] = """
            public class Solution {
                public bool IsSubtree(TreeNode root, TreeNode subRoot) {
                    if (root == null) return false;
                    return Same(root, subRoot) || IsSubtree(root.left, subRoot) || IsSubtree(root.right, subRoot);
                }
                private bool Same(TreeNode a, TreeNode b) {
                    if (a == null || b == null) return a == b;
                    return a.val == b.val && Same(a.left, b.left) && Same(a.right, b.right);
                }
            }
            """,
        ["kth-largest-element-in-a-stream"] = """
            public class KthLargest {
                private readonly PriorityQueue<int, int> heap = new();
                private readonly int k;
                public KthLargest(int k, int[] nums) {
                    this.k = k;
                    foreach (int x in nums) Add(x);
                }
                public int Add(int val) {
                    heap.Enqueue(val, val);
                    if (heap.Count > k) heap.Dequeue();
                    return heap.Peek();
                }
            }
            """,
        ["last-stone-weight"] = """
            public class Solution {
                public int LastStoneWeight(int[] stones) {
                    var heap = new PriorityQueue<int, int>();
                    foreach (int s in stones) heap.Enqueue(s, -s);
                    while (heap.Count > 1) {
                        int y = heap.Dequeue(), x = heap.Dequeue();
                        if (y != x) heap.Enqueue(y - x, -(y - x));
                    }
                    return heap.Count == 0 ? 0 : heap.Peek();
                }
            }
            """,
        ["climbing-stairs"] = """
            public class Solution {
                public int ClimbStairs(int n) {
                    int[] dp = new int[n + 1];
                    dp[0] = 1; dp[1] = 1;
                    for (int i = 2; i <= n; i++) dp[i] = dp[i - 1] + dp[i - 2];
                    return dp[n];
                }
            }
            """,
        ["min-cost-climbing-stairs"] = """
            public class Solution {
                public int MinCostClimbingStairs(int[] cost) {
                    int a = 0, b = 0;
                    for (int i = 2; i <= cost.Length; i++) {
                        int c = Math.Min(b + cost[i - 1], a + cost[i - 2]);
                        a = b; b = c;
                    }
                    return b;
                }
            }
            """,
        ["meeting-rooms"] = """
            public class Solution {
                public bool CanAttendMeetings(int[][] intervals) {
                    Array.Sort(intervals, (x, y) => x[0].CompareTo(y[0]));
                    for (int i = 1; i < intervals.Length; i++) {
                        if (intervals[i][0] < intervals[i - 1][1]) return false;
                    }
                    return true;
                }
            }
            """,
        ["happy-number"] = """
            public class Solution {
                public bool IsHappy(int n) {
                    var seen = new HashSet<int>();
                    while (n != 1 && seen.Add(n)) {
                        int sum = 0;
                        while (n > 0) { int d = n % 10; sum += d * d; n /= 10; }
                        n = sum;
                    }
                    return n == 1;
                }
            }
            """,
        ["plus-one"] = """
            public class Solution {
                public int[] PlusOne(int[] digits) {
                    for (int i = digits.Length - 1; i >= 0; i--) {
                        if (digits[i] < 9) { digits[i]++; return digits; }
                        digits[i] = 0;
                    }
                    int[] result = new int[digits.Length + 1];
                    result[0] = 1;
                    return result;
                }
            }
            """,
        ["single-number"] = """
            public class Solution {
                public int SingleNumber(int[] nums) {
                    int x = 0;
                    foreach (int n in nums) x ^= n;
                    return x;
                }
            }
            """,
        ["number-of-1-bits"] = """
            public class Solution {
                public int HammingWeight(int n) {
                    int count = 0;
                    while (n != 0) { n &= n - 1; count++; }
                    return count;
                }
            }
            """,
        ["counting-bits"] = """
            public class Solution {
                public int[] CountBits(int n) {
                    int[] ans = new int[n + 1];
                    for (int i = 1; i <= n; i++) ans[i] = ans[i >> 1] + (i & 1);
                    return ans;
                }
            }
            """,
        ["reverse-bits"] = """
            public class Solution {
                public uint reverseBits(uint n) {
                    uint result = 0;
                    for (int i = 0; i < 32; i++) { result = (result << 1) | (n & 1); n >>= 1; }
                    return result;
                }
            }
            """,
        ["missing-number"] = """
            public class Solution {
                public int MissingNumber(int[] nums) {
                    int n = nums.Length, sum = n * (n + 1) / 2;
                    foreach (int x in nums) sum -= x;
                    return sum;
                }
            }
            """,
    };
}
