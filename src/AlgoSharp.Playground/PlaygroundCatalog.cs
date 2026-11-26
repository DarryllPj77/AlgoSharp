using System.Numerics;
using System.Text.Json;
using AlgoSharp.Playground.Compilation;
using static AlgoSharp.Playground.ProblemInputs;
using static AlgoSharp.Playground.References;

namespace AlgoSharp.Playground;

/// <summary>The Solve It problem set (LeetCode Easy, NeetCode 150 order).</summary>
public static class PlaygroundCatalog
{
    private const string ListDef = """
        /**
         * ListNode is provided (you don't need to declare it):
         * public class ListNode {
         *     public int val;
         *     public ListNode next;
         *     public ListNode(int val = 0, ListNode next = null) { ... }
         * }
         */
        """;

    private const string TreeDef = """
        /**
         * TreeNode is provided (you don't need to declare it):
         * public class TreeNode {
         *     public int val;
         *     public TreeNode left;
         *     public TreeNode right;
         *     public TreeNode(int val = 0, TreeNode left = null, TreeNode right = null) { ... }
         * }
         */
        """;

    private static string Starter(string signature, string body, string? header = null) =>
        (header is null ? "" : header + "\n") + $$"""
        public class Solution
        {
            {{signature}}
            {
                // Write your solution here.
                {{body}}
            }
        }
        """;

    private static ParamSpec P(string name, ArgKind kind, string placeholder) => new(name, kind, placeholder);

    private static ProblemExample E(string output, string? why, params string[] args) => new(args, output, why);

    private static EntrySignature Sig(string name, string description, params ITypeSymbolMatcher[] ps) => new(name, ps, description);

    private static string Arr(Random r, int min, int max, int lo, int hi) =>
        "[" + string.Join(", ", Enumerable.Range(0, r.Next(min, max + 1)).Select(_ => r.Next(lo, hi + 1))) + "]";

    public static IReadOnlyList<string> Categories { get; } =
    [
        "Arrays & Hashing", "Two Pointers", "Sliding Window", "Stack", "Binary Search", "Linked List",
        "Trees", "Heap / Priority Queue", "1-D Dynamic Programming", "Intervals", "Math & Geometry", "Bit Manipulation",
    ];

    public static IReadOnlyList<PlaygroundProblem> All { get; } =
    [
        // ------------------------------------------------------------ Arrays & Hashing
        new("contains-duplicate", "Contains Duplicate", 217, "Arrays & Hashing",
            "Given an integer array nums, return true if any value appears at least twice in the array, and return false if every element is distinct.",
            ["1 ≤ nums.length ≤ 20 here (10⁵ on LeetCode)", "−10⁹ ≤ nums[i] ≤ 10⁹"],
            [P("nums", ArgKind.IntArray, "[1, 2, 3, 1]")],
            [E("true", "The value 1 appears at indices 0 and 3.", "[1, 2, 3, 1]"), E("false", "All elements are distinct.", "[1, 2, 3, 4]"), E("true", null, "[1, 1, 1, 3, 3, 4, 3, 2, 4, 2]")],
            [["[5]"], ["[-1, 0, -1]"]],
            Sig("ContainsDuplicate", "bool ContainsDuplicate(int[] nums)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public bool ContainsDuplicate(int[] nums)", "return false;"),
            ["A direct approach compares every pair of elements. How many pairs are there?", "If the array were sorted, where would duplicates be?", "A HashSet<int> can tell you in O(1) whether you've seen a value before."],
            a => V(Ints(a[0]).Distinct().Count() != a[0].GetArrayLength()))
        { RandomCase = r => [Arr(r, 2, 10, 0, 12)] },

        new("valid-anagram", "Valid Anagram", 242, "Arrays & Hashing",
            "Given two strings s and t, return true if t is an anagram of s, and false otherwise. An anagram uses exactly the same letters, the same number of times, in any order.",
            ["1 ≤ s.length, t.length ≤ 40 here", "s and t consist of lowercase English letters"],
            [P("s", ArgKind.String, "anagram"), P("t", ArgKind.String, "nagaram")],
            [E("true", null, "\"anagram\"", "\"nagaram\""), E("false", null, "\"rat\"", "\"car\"")],
            [["\"a\"", "\"ab\""], ["\"listen\"", "\"silent\""]],
            Sig("IsAnagram", "bool IsAnagram(string s, string t)", global::AlgoSharp.Playground.Sig.String, global::AlgoSharp.Playground.Sig.String),
            Starter("public bool IsAnagram(string s, string t)", "return false;"),
            ["If the lengths differ, can they be anagrams?", "Sorting both strings and comparing works. What does it cost?", "Count letters: an int[26] where s adds and t subtracts. All zeros means anagram."],
            a => V(Anagram(S(a[0]), S(a[1]))))
        { Validate = a => S(a[0]).All(char.IsAsciiLetterLower) && S(a[1]).All(char.IsAsciiLetterLower) ? null : "Use lowercase letters a–z only." },

        new("two-sum", "Two Sum", 1, "Arrays & Hashing",
            "Given an array of integers nums and an integer target, return the indices of the two numbers that add up to target. Each input has at most one solution, and you may not use the same element twice. Return an empty array if no pair exists.",
            ["2 ≤ nums.length ≤ 20 here", "Negative numbers and duplicates are allowed", "Any valid pair of indices is accepted"],
            [P("nums", ArgKind.IntArray, "[2, 7, 11, 15]"), P("target", ArgKind.Int, "9")],
            [E("[0, 1]", "nums[0] + nums[1] = 2 + 7 = 9.", "[2, 7, 11, 15]", "9"), E("[1, 2]", "Using 3 twice is not allowed.", "[3, 2, 4]", "6"), E("[0, 1]", null, "[3, 3]", "6")],
            [["[-3, 4, 3, 90]", "0"], ["[1, 2, 5]", "100"]],
            Sig("TwoSum", "int[] TwoSum(int[] nums, int target)", global::AlgoSharp.Playground.Sig.IntArray, global::AlgoSharp.Playground.Sig.Int),
            Starter("public int[] TwoSum(int[] nums, int target)", "return new int[0];"),
            ["Start simple: can you check every pair of positions (i, j)? Never pair an element with itself.", "Once the pairs version passes, count how many pairs it checks for n numbers.", "For each nums[i] you need exactly one value: target − nums[i]. Could you remember values you've already seen?", "A Dictionary<int, int> can map a value to its index. Check it before inserting the current number."],
            a => TwoSumAnswer(Ints(a[0]), a[1].GetInt32()))
        {
            Checker = CheckTwoSum,
            RandomCase = r =>
            {
                var nums = Enumerable.Range(0, r.Next(4, 9)).Select(_ => r.Next(-10, 30)).ToArray();
                int i = r.Next(nums.Length), j = (i + 1 + r.Next(nums.Length - 1)) % nums.Length;
                return ["[" + string.Join(", ", nums) + "]", (nums[i] + nums[j]).ToString()];
            },
        },

        // ------------------------------------------------------------ Two Pointers
        new("valid-palindrome", "Valid Palindrome", 125, "Two Pointers",
            "A phrase is a palindrome if, after converting all uppercase letters into lowercase and removing all non-alphanumeric characters, it reads the same forward and backward. Given a string s, return true if it is a palindrome, or false otherwise.",
            ["1 ≤ s.length ≤ 40 here", "s consists of printable ASCII characters"],
            [P("s", ArgKind.String, "A man, a plan, a canal: Panama")],
            [E("true", "\"amanaplanacanalpanama\" is a palindrome.", "\"A man, a plan, a canal: Panama\""), E("false", "\"raceacar\" is not a palindrome.", "\"race a car\""), E("true", "After removing non-alphanumerics the string is empty, which reads the same both ways.", "\" \"")],
            [["\"0P\""], ["\"No lemon, no melon\""]],
            Sig("IsPalindrome", "bool IsPalindrome(string s)", global::AlgoSharp.Playground.Sig.String),
            Starter("public bool IsPalindrome(string s)", "return false;"),
            ["char.IsLetterOrDigit and char.ToLower are your friends.", "Building a cleaned copy and reversing it works, but uses extra memory.", "Two pointers: start at both ends, skip non-alphanumerics, and compare while moving inward."],
            a => V(Palindrome(S(a[0])))),

        // ------------------------------------------------------------ Sliding Window
        new("best-time-to-buy-and-sell-stock", "Best Time to Buy and Sell Stock", 121, "Sliding Window",
            "You are given an array prices where prices[i] is the price of a stock on day i. Choose one day to buy and a later day to sell to maximize your profit. Return the maximum profit, or 0 if no profit is possible.",
            ["1 ≤ prices.length ≤ 20 here", "0 ≤ prices[i] ≤ 10⁴"],
            [P("prices", ArgKind.IntArray, "[7, 1, 5, 3, 6, 4]")],
            [E("5", "Buy on day 1 (price 1), sell on day 4 (price 6).", "[7, 1, 5, 3, 6, 4]"), E("0", "Prices only fall, so no profitable trade exists.", "[7, 6, 4, 3, 1]")],
            [["[2, 4, 1]"], ["[3]"]],
            Sig("MaxProfit", "int MaxProfit(int[] prices)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int MaxProfit(int[] prices)", "return 0;"),
            ["Brute force: try every buy day with every later sell day.", "For each sell day, the best buy day is the cheapest day before it.", "Track the minimum price so far and the best profit in a single pass."],
            a => V(MaxProfit(Ints(a[0]))))
        { RandomCase = r => [Arr(r, 2, 10, 1, 20)], Validate = a => Ints(a[0]).All(x => x >= 0) && a[0].GetArrayLength() > 0 ? null : "Prices must be non-negative, with at least one day." },

        // ------------------------------------------------------------ Stack
        new("valid-parentheses", "Valid Parentheses", 20, "Stack",
            "Given a string s containing just the characters '(', ')', '{', '}', '[' and ']', determine if the input string is valid. Open brackets must be closed by the same type of bracket, in the correct order, and every close bracket must have a matching open bracket.",
            ["1 ≤ s.length ≤ 40 here", "s consists only of the characters ()[]{}"],
            [P("s", ArgKind.String, "()[]{}")],
            [E("true", null, "\"()\""), E("true", null, "\"()[]{}\""), E("false", null, "\"(]\""), E("true", null, "\"([])\"")],
            [["\"(((\""], ["\"]\""], ["\"{[]}\""]],
            Sig("IsValid", "bool IsValid(string s)", global::AlgoSharp.Playground.Sig.String),
            Starter("public bool IsValid(string s)", "return false;"),
            ["The most recently opened bracket must be closed first. Which data structure works that way?", "Push opening brackets onto a Stack<char>.", "On a closing bracket, the stack must be non-empty and its top must be the matching opener. At the end the stack must be empty."],
            a => V(Parentheses(S(a[0]))))
        { Validate = a => S(a[0]).All(c => "()[]{}".Contains(c)) ? null : "Use only the characters ()[]{}." },

        // ------------------------------------------------------------ Binary Search
        new("binary-search", "Binary Search", 704, "Binary Search",
            "Given an array of integers nums sorted in ascending order and an integer target, return the index of target in nums, or -1 if it does not exist. Aim for O(log n) time.",
            ["1 ≤ nums.length ≤ 20 here", "All values in nums are unique", "nums is sorted in ascending order"],
            [P("nums", ArgKind.IntArray, "[-1, 0, 3, 5, 9, 12]"), P("target", ArgKind.Int, "9")],
            [E("4", "9 exists in nums at index 4.", "[-1, 0, 3, 5, 9, 12]", "9"), E("-1", "2 does not exist in nums.", "[-1, 0, 3, 5, 9, 12]", "2")],
            [["[5]", "5"], ["[1, 3, 5, 7, 9, 11, 13]", "13"]],
            Sig("Search", "int Search(int[] nums, int target)", global::AlgoSharp.Playground.Sig.IntArray, global::AlgoSharp.Playground.Sig.Int),
            Starter("public int Search(int[] nums, int target)", "return -1;"),
            ["A linear scan works, but ignores that nums is sorted.", "Look at the middle element: which half can still contain the target?", "Keep lo and hi indices; compute mid = lo + (hi - lo) / 2 and shrink the range until lo > hi."],
            a => V(Array.IndexOf(Ints(a[0]), a[1].GetInt32())))
        {
            Validate = a =>
            {
                var n = Ints(a[0]);
                return n.Length > 0 && n.Zip(n.Skip(1)).All(p => p.First < p.Second) ? null : "nums must be non-empty, strictly increasing (sorted, no duplicates).";
            },
            RandomCase = r =>
            {
                var n = Enumerable.Range(-5, 30).OrderBy(_ => r.Next()).Take(r.Next(4, 12)).Order().ToArray();
                return ["[" + string.Join(", ", n) + "]", (r.Next(3) == 0 ? r.Next(-6, 26) : n[r.Next(n.Length)]).ToString()];
            },
        },

        // ------------------------------------------------------------ Linked List
        new("reverse-linked-list", "Reverse Linked List", 206, "Linked List",
            "Given the head of a singly linked list, reverse the list, and return the reversed list.",
            ["0 ≤ number of nodes ≤ 20 here", "−5000 ≤ Node.val ≤ 5000"],
            [P("head", ArgKind.LinkedList, "[1, 2, 3, 4, 5]")],
            [E("[5, 4, 3, 2, 1]", null, "[1, 2, 3, 4, 5]"), E("[2, 1]", null, "[1, 2]"), E("[]", null, "[]")],
            [["[7]"]],
            Sig("ReverseList", "ListNode ReverseList(ListNode head)", global::AlgoSharp.Playground.Sig.ListNode),
            Starter("public ListNode ReverseList(ListNode head)", "return head;", ListDef),
            ["Walk the list with a curr pointer. What should curr.next point to after reversing?", "You'll need to remember the previous node, and save curr.next before you overwrite it.", "prev = null; while (curr != null) { next = curr.next; curr.next = prev; prev = curr; curr = next; } return prev."],
            a => V(Ints(a[0]).Reverse())),

        new("merge-two-sorted-lists", "Merge Two Sorted Lists", 21, "Linked List",
            "You are given the heads of two sorted linked lists list1 and list2. Merge the two lists into one sorted list made by splicing together the nodes of the first two lists, and return its head.",
            ["0 ≤ number of nodes in each list ≤ 20 here", "Both lists are sorted in non-decreasing order"],
            [P("list1", ArgKind.LinkedList, "[1, 2, 4]"), P("list2", ArgKind.LinkedList, "[1, 3, 4]")],
            [E("[1, 1, 2, 3, 4, 4]", null, "[1, 2, 4]", "[1, 3, 4]"), E("[]", null, "[]", "[]"), E("[0]", null, "[]", "[0]")],
            [["[5]", "[1, 2, 4]"]],
            Sig("MergeTwoLists", "ListNode MergeTwoLists(ListNode list1, ListNode list2)", global::AlgoSharp.Playground.Sig.ListNode, global::AlgoSharp.Playground.Sig.ListNode),
            Starter("public ListNode MergeTwoLists(ListNode list1, ListNode list2)", "return null;", ListDef),
            ["Compare the two heads; the smaller one comes first.", "A dummy node in front of the result makes the code simpler: you always append to tail.next.", "When one list runs out, attach the rest of the other list."],
            a => V(Ints(a[0]).Concat(Ints(a[1])).Order()))
        { Validate = a => IsSorted(Ints(a[0])) && IsSorted(Ints(a[1])) ? null : "Both lists must be sorted in non-decreasing order." },

        new("linked-list-cycle", "Linked List Cycle", 141, "Linked List",
            "Given head, the head of a linked list, determine if the linked list has a cycle in it. Internally, pos is the index of the node that the tail's next pointer connects to (-1 means no cycle); pos is not passed to your method. Return true if there is a cycle, otherwise false.",
            ["0 ≤ number of nodes ≤ 20 here", "pos is -1 or a valid index in the list"],
            [P("head", ArgKind.LinkedList, "[3, 2, 0, -4]"), P("pos", ArgKind.Int, "1")],
            [E("true", "The tail connects back to the node at index 1.", "[3, 2, 0, -4]", "1"), E("true", "The tail connects back to index 0.", "[1, 2]", "0"), E("false", "There is no cycle.", "[1]", "-1")],
            [["[]", "-1"], ["[1, 2, 3, 4]", "-1"]],
            Sig("HasCycle", "bool HasCycle(ListNode head)", global::AlgoSharp.Playground.Sig.ListNode),
            Starter("public bool HasCycle(ListNode head)", "return false;", ListDef),
            ["Remember visited nodes in a HashSet<ListNode>. Seeing one again means a cycle.", "Can you do it with O(1) memory?", "Floyd's tortoise and hare: slow moves 1 step, fast moves 2. If they ever meet, there is a cycle."],
            a => V(a[1].GetInt32() >= 0))
        {
            Validate = a => a[1].GetInt32() >= -1 && a[1].GetInt32() < Math.Max(1, a[0].GetArrayLength()) && (a[0].GetArrayLength() > 0 || a[1].GetInt32() == -1) ? null : "pos must be -1 or an index of the list.",
            ToRunnerArgs = a => [JsonSerializer.SerializeToElement(new { values = Ints(a[0]), pos = a[1].GetInt32() })],
        },

        // ------------------------------------------------------------ Trees
        new("invert-binary-tree", "Invert Binary Tree", 226, "Trees",
            "Given the root of a binary tree, invert the tree (mirror it left to right), and return its root.",
            ["0 ≤ number of nodes ≤ 31 here", "Trees are written in level order, with null for missing children"],
            [P("root", ArgKind.Tree, "[4, 2, 7, 1, 3, 6, 9]")],
            [E("[4, 7, 2, 9, 6, 3, 1]", null, "[4, 2, 7, 1, 3, 6, 9]"), E("[2, 3, 1]", null, "[2, 1, 3]"), E("[]", null, "[]")],
            [["[1, 2]"]],
            Sig("InvertTree", "TreeNode InvertTree(TreeNode root)", global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public TreeNode InvertTree(TreeNode root)", "return root;", TreeDef),
            ["Inverting a tree = swapping the left and right child of every node.", "Recursion: invert the left subtree, invert the right subtree, then swap them.", "The base case: an empty tree (null) is its own inversion."],
            a => TreeJson(Invert(Tree(a[0])))),

        new("maximum-depth-of-binary-tree", "Maximum Depth of Binary Tree", 104, "Trees",
            "Given the root of a binary tree, return its maximum depth: the number of nodes along the longest path from the root down to the farthest leaf.",
            ["0 ≤ number of nodes ≤ 31 here"],
            [P("root", ArgKind.Tree, "[3, 9, 20, null, null, 15, 7]")],
            [E("3", null, "[3, 9, 20, null, null, 15, 7]"), E("2", null, "[1, null, 2]")],
            [["[]"], ["[1, 2, null, 3, null, 4]"]],
            Sig("MaxDepth", "int MaxDepth(TreeNode root)", global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public int MaxDepth(TreeNode root)", "return 0;", TreeDef),
            ["What is the depth of an empty tree?", "The depth of a node is 1 + the larger depth of its two subtrees.", "Recursion follows directly; a BFS counting levels with a Queue<TreeNode> also works."],
            a => V(Height(Tree(a[0])))),

        new("diameter-of-binary-tree", "Diameter of Binary Tree", 543, "Trees",
            "Given the root of a binary tree, return the length of the diameter of the tree: the number of edges on the longest path between any two nodes. The path may or may not pass through the root.",
            ["1 ≤ number of nodes ≤ 31 here"],
            [P("root", ArgKind.Tree, "[1, 2, 3, 4, 5]")],
            [E("3", "The path [4, 2, 1, 3] or [5, 2, 1, 3] has 3 edges.", "[1, 2, 3, 4, 5]"), E("1", null, "[1, 2]")],
            [["[1, 2, null, 3, 4, 5, null, null, 6]"]],
            Sig("DiameterOfBinaryTree", "int DiameterOfBinaryTree(TreeNode root)", global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public int DiameterOfBinaryTree(TreeNode root)", "return 0;", TreeDef),
            ["The longest path through a node uses the height of its left subtree plus the height of its right subtree.", "Computing height separately for every node repeats work: O(n²).", "One DFS that returns the height and updates a best-diameter variable as it goes is O(n)."],
            a => V(Diameter(Tree(a[0]))))
        { Validate = a => Tree(a[0]) is null ? "The tree needs at least one node." : null },

        new("balanced-binary-tree", "Balanced Binary Tree", 110, "Trees",
            "Given a binary tree, determine if it is height-balanced: for every node, the heights of its left and right subtrees differ by at most one.",
            ["0 ≤ number of nodes ≤ 31 here"],
            [P("root", ArgKind.Tree, "[3, 9, 20, null, null, 15, 7]")],
            [E("true", null, "[3, 9, 20, null, null, 15, 7]"), E("false", null, "[1, 2, 2, 3, 3, null, null, 4, 4]"), E("true", null, "[]")],
            [["[1, 2, 2, 3, null, null, 3, 4, null, null, 4]"]],
            Sig("IsBalanced", "bool IsBalanced(TreeNode root)", global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public bool IsBalanced(TreeNode root)", "return false;", TreeDef),
            ["Write a helper that returns the height of a subtree.", "Checking |height(left) − height(right)| ≤ 1 at every node is correct but recomputes heights.", "Return −1 from the height helper when a subtree is unbalanced, so one pass decides everything."],
            a => V(Balanced(Tree(a[0])))),

        new("same-tree", "Same Tree", 100, "Trees",
            "Given the roots of two binary trees p and q, return true if they are the same: structurally identical, with the same values in the same nodes.",
            ["0 ≤ number of nodes in each tree ≤ 31 here"],
            [P("p", ArgKind.Tree, "[1, 2, 3]"), P("q", ArgKind.Tree, "[1, 2, 3]")],
            [E("true", null, "[1, 2, 3]", "[1, 2, 3]"), E("false", null, "[1, 2]", "[1, null, 2]"), E("false", null, "[1, 2, 1]", "[1, 1, 2]")],
            [["[]", "[]"]],
            Sig("IsSameTree", "bool IsSameTree(TreeNode p, TreeNode q)", global::AlgoSharp.Playground.Sig.TreeNode, global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public bool IsSameTree(TreeNode p, TreeNode q)", "return false;", TreeDef),
            ["Two empty trees are the same. One empty and one not are different.", "Otherwise the root values must match…", "…and the left subtrees must be the same, and the right subtrees must be the same."],
            a => V(Same(Tree(a[0]), Tree(a[1])))),

        new("subtree-of-another-tree", "Subtree of Another Tree", 572, "Trees",
            "Given the roots of two binary trees root and subRoot, return true if there is a subtree of root with the same structure and node values as subRoot. A subtree consists of a node and all of its descendants.",
            ["1 ≤ number of nodes in root ≤ 31 here", "1 ≤ number of nodes in subRoot ≤ 31 here"],
            [P("root", ArgKind.Tree, "[3, 4, 5, 1, 2]"), P("subRoot", ArgKind.Tree, "[4, 1, 2]")],
            [E("true", null, "[3, 4, 5, 1, 2]", "[4, 1, 2]"), E("false", "The node 4's subtree also contains 0, so it isn't identical.", "[3, 4, 5, 1, 2, null, null, null, null, 0]", "[4, 1, 2]")],
            [["[1, 1]", "[1]"]],
            Sig("IsSubtree", "bool IsSubtree(TreeNode root, TreeNode subRoot)", global::AlgoSharp.Playground.Sig.TreeNode, global::AlgoSharp.Playground.Sig.TreeNode),
            Starter("public bool IsSubtree(TreeNode root, TreeNode subRoot)", "return false;", TreeDef),
            ["Solve Same Tree first: you'll reuse it.", "At every node of root, ask: is the tree starting here the same as subRoot?", "Recurse: IsSame(root, subRoot) || IsSubtree(root.left, subRoot) || IsSubtree(root.right, subRoot)."],
            a => V(Subtree(Tree(a[0]), Tree(a[1]))))
        { Validate = a => Tree(a[0]) is null || Tree(a[1]) is null ? "Both trees need at least one node." : null },

        // ------------------------------------------------------------ Heap / Priority Queue
        new("kth-largest-element-in-a-stream", "Kth Largest Element in a Stream", 703, "Heap / Priority Queue",
            "Design a class to find the kth largest element in a stream. KthLargest(int k, int[] nums) initializes the object with k and the initial scores; int Add(int val) adds val to the stream and returns the kth largest element so far. The test calls Add once for each value in adds.",
            ["1 ≤ k ≤ nums.length + 1", "0 ≤ nums.length ≤ 20 here", "After each Add there are at least k elements"],
            [P("k", ArgKind.Int, "3"), P("nums", ArgKind.IntArray, "[4, 5, 8, 2]"), P("adds", ArgKind.IntArray, "[3, 5, 10, 9, 4]")],
            [E("[4, 5, 5, 8, 8]", "Results of Add(3), Add(5), Add(10), Add(9), Add(4).", "3", "[4, 5, 8, 2]", "[3, 5, 10, 9, 4]")],
            [["1", "[]", "[-3, -2, -4, 0, 4]"], ["2", "[0]", "[-1, 1, -2, -4, 3]"]],
            new EntrySignature("Add", [global::AlgoSharp.Playground.Sig.Int], "int Add(int val)", DesignClass: "KthLargest"),
            """
            public class KthLargest
            {
                public KthLargest(int k, int[] nums)
                {
                    // Store what you need.
                }

                public int Add(int val)
                {
                    // Return the kth largest value seen so far.
                    return 0;
                }
            }
            """,
            ["Sorting all values after every Add works, but costs O(n log n) per call.", "You only ever need the k largest values. The smallest of those is the answer.", "Keep a min-heap of size k: PriorityQueue<int, int>. Enqueue, and Dequeue when it holds more than k."],
            a => V(KthLargest(a[0].GetInt32(), Ints(a[1]), Ints(a[2]))))
        { Validate = a => a[0].GetInt32() >= 1 && a[0].GetInt32() <= a[1].GetArrayLength() + 1 && a[2].GetArrayLength() > 0 ? null : "Need 1 ≤ k ≤ nums.length + 1 and at least one value in adds." },

        new("last-stone-weight", "Last Stone Weight", 1046, "Heap / Priority Queue",
            "You are given an array of stone weights. Each turn, smash the two heaviest stones x ≤ y together: if x == y both are destroyed, otherwise the stone of weight y − x remains. Return the weight of the last remaining stone, or 0 if none are left.",
            ["1 ≤ stones.length ≤ 20 here", "1 ≤ stones[i] ≤ 1000"],
            [P("stones", ArgKind.IntArray, "[2, 7, 4, 1, 8, 1]")],
            [E("1", "Smash 7 and 8 → 1; then 2 and 4 → 2; then 2 and 1 → 1; then 1 and 1 → 0; one stone of weight 1 remains.", "[2, 7, 4, 1, 8, 1]"), E("1", null, "[1]")],
            [["[3, 3]"], ["[10, 4, 2, 10]"]],
            Sig("LastStoneWeight", "int LastStoneWeight(int[] stones)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int LastStoneWeight(int[] stones)", "return 0;"),
            ["Simulate the game exactly as described.", "Re-sorting every turn is O(n² log n). You only need the two heaviest stones each time.", "A max-heap: PriorityQueue<int, int> with priority −weight (or a reversed comparer)."],
            a => V(LastStone(Ints(a[0]))))
        { Validate = a => a[0].GetArrayLength() > 0 && Ints(a[0]).All(x => x >= 1) ? null : "At least one stone, all weights ≥ 1.", RandomCase = r => [Arr(r, 1, 8, 1, 15)] },

        // ------------------------------------------------------------ 1-D DP
        new("climbing-stairs", "Climbing Stairs", 70, "1-D Dynamic Programming",
            "You are climbing a staircase that takes n steps to reach the top. Each time you can climb 1 or 2 steps. In how many distinct ways can you climb to the top?",
            ["1 ≤ n ≤ 30 here (45 on LeetCode)"],
            [P("n", ArgKind.Int, "3")],
            [E("2", "1 + 1, or 2.", "2"), E("3", "1 + 1 + 1, 1 + 2, or 2 + 1.", "3")],
            [["1"], ["10"]],
            Sig("ClimbStairs", "int ClimbStairs(int n)", global::AlgoSharp.Playground.Sig.Int),
            Starter("public int ClimbStairs(int n)", "return 0;"),
            ["The last move is either 1 step (from n − 1) or 2 steps (from n − 2).", "So ways(n) = ways(n − 1) + ways(n − 2). Naive recursion recomputes the same values exponentially often.", "Fill an array dp[0..n] bottom-up, or keep just the last two values."],
            a => V(ClimbStairs(a[0].GetInt32())))
        { Validate = a => a[0].GetInt32() is >= 1 and <= 30 ? null : "n must be between 1 and 30.", RandomCase = r => [r.Next(1, 16).ToString()] },

        new("min-cost-climbing-stairs", "Min Cost Climbing Stairs", 746, "1-D Dynamic Programming",
            "You are given an array cost where cost[i] is the cost of the ith step. Once you pay the cost, you can climb one or two steps. You can start at step 0 or step 1. Return the minimum cost to reach the top (just past the last step).",
            ["2 ≤ cost.length ≤ 20 here", "0 ≤ cost[i] ≤ 999"],
            [P("cost", ArgKind.IntArray, "[10, 15, 20]")],
            [E("15", "Start at index 1, pay 15 and climb two steps to the top.", "[10, 15, 20]"), E("6", null, "[1, 100, 1, 1, 1, 100, 1, 1, 100, 1]")],
            [["[0, 0]"], ["[5, 1, 5, 1, 5]"]],
            Sig("MinCostClimbingStairs", "int MinCostClimbingStairs(int[] cost)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int MinCostClimbingStairs(int[] cost)", "return 0;"),
            ["To stand on step i you came from step i − 1 or i − 2.", "Let dp[i] = minimum cost to reach step i. Then dp[i] = min(dp[i−1] + cost[i−1], dp[i−2] + cost[i−2]).", "The answer is dp[cost.Length]; you only need the last two dp values."],
            a => V(MinCost(Ints(a[0]))))
        { Validate = a => a[0].GetArrayLength() >= 2 && Ints(a[0]).All(x => x >= 0) ? null : "At least two steps, all costs ≥ 0.", RandomCase = r => [Arr(r, 2, 10, 0, 20)] },

        // ------------------------------------------------------------ Intervals
        new("meeting-rooms", "Meeting Rooms", 252, "Intervals",
            "Given an array of meeting time intervals where intervals[i] = [start, end], determine if a person could attend all meetings (no two meetings overlap). A meeting ending at time t doesn't overlap one starting at t.",
            ["0 ≤ intervals.length ≤ 20 here", "start < end for every interval"],
            [P("intervals", ArgKind.IntMatrix, "[[0, 30], [5, 10], [15, 20]]")],
            [E("false", "[0, 30] overlaps both other meetings.", "[[0, 30], [5, 10], [15, 20]]"), E("true", null, "[[7, 10], [2, 4]]")],
            [["[]"], ["[[1, 5], [5, 8]]"]],
            Sig("CanAttendMeetings", "bool CanAttendMeetings(int[][] intervals)", global::AlgoSharp.Playground.Sig.IntMatrix),
            Starter("public bool CanAttendMeetings(int[][] intervals)", "return false;"),
            ["Comparing every pair of meetings works: O(n²).", "If the meetings were sorted by start time, which pairs would you need to check?", "Sort by start (Array.Sort with a comparison), then check that each meeting starts no earlier than the previous one ends."],
            a => V(CanAttend(a[0])))
        { Validate = a => a[0].EnumerateArray().All(r => Ints(r) is [var s, var e] && s < e) ? null : "Every interval needs start < end." },

        // ------------------------------------------------------------ Math & Geometry
        new("happy-number", "Happy Number", 202, "Math & Geometry",
            "A happy number is defined by this process: replace the number by the sum of the squares of its digits, and repeat. If the process reaches 1, the number is happy; if it loops forever in a cycle that doesn't include 1, it isn't. Return true if n is happy.",
            ["1 ≤ n ≤ 2³¹ − 1"],
            [P("n", ArgKind.Int, "19")],
            [E("true", "1² + 9² = 82 → 8² + 2² = 68 → 6² + 8² = 100 → 1² + 0² + 0² = 1.", "19"), E("false", null, "2")],
            [["1"], ["7"]],
            Sig("IsHappy", "bool IsHappy(int n)", global::AlgoSharp.Playground.Sig.Int),
            Starter("public bool IsHappy(int n)", "return false;"),
            ["Write a helper that returns the sum of the squares of the digits (use % 10 and / 10).", "If a number repeats, you're in a cycle and will never reach 1.", "Track seen numbers in a HashSet<int>, or use slow/fast pointers like cycle detection."],
            a => V(Happy(a[0].GetInt32())))
        { Validate = a => a[0].GetInt32() >= 1 ? null : "n must be at least 1.", RandomCase = r => [r.Next(1, 100).ToString()] },

        new("plus-one", "Plus One", 66, "Math & Geometry",
            "You are given a large integer represented as an array of digits, most significant first, with no leading zeros. Increment the integer by one and return the resulting array of digits.",
            ["1 ≤ digits.length ≤ 20 here", "0 ≤ digits[i] ≤ 9", "No leading zeros (except the number 0 itself)"],
            [P("digits", ArgKind.IntArray, "[1, 2, 3]")],
            [E("[1, 2, 4]", "123 + 1 = 124.", "[1, 2, 3]"), E("[4, 3, 2, 2]", null, "[4, 3, 2, 1]"), E("[1, 0]", "9 + 1 = 10.", "[9]")],
            [["[9, 9, 9]"], ["[0]"]],
            Sig("PlusOne", "int[] PlusOne(int[] digits)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int[] PlusOne(int[] digits)", "return digits;"),
            ["Add one to the last digit. When does that need a carry?", "Walk from the end: a 9 becomes 0 and the carry continues; any other digit just increments and you're done.", "If every digit was 9, you need a new array one longer, starting with 1."],
            a => V(PlusOne(Ints(a[0]))))
        { Validate = a => Ints(a[0]) is { Length: > 0 } d && d.All(x => x is >= 0 and <= 9) && (d[0] != 0 || d.Length == 1) ? null : "Digits 0–9, at least one, no leading zero." },

        // ------------------------------------------------------------ Bit Manipulation
        new("single-number", "Single Number", 136, "Bit Manipulation",
            "Given a non-empty array of integers nums, every element appears twice except for one. Find that single one, using linear time and constant extra space.",
            ["1 ≤ nums.length ≤ 19 here (odd)", "Every element appears exactly twice except one"],
            [P("nums", ArgKind.IntArray, "[4, 1, 2, 1, 2]")],
            [E("1", null, "[2, 2, 1]"), E("4", null, "[4, 1, 2, 1, 2]"), E("1", null, "[1]")],
            [["[-1, 3, 3]"]],
            Sig("SingleNumber", "int SingleNumber(int[] nums)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int SingleNumber(int[] nums)", "return 0;"),
            ["Counting with a Dictionary works but uses O(n) memory.", "What is x ^ x? And x ^ 0?", "XOR all numbers together: pairs cancel out and the single number remains."],
            a => V(Ints(a[0]).Aggregate(0, (x, y) => x ^ y)))
        { Validate = a => Ints(a[0]).GroupBy(x => x).Count(g => g.Count() == 1) == 1 && Ints(a[0]).GroupBy(x => x).All(g => g.Count() is 1 or 2) ? null : "Every value must appear exactly twice, except one value that appears once." },

        new("number-of-1-bits", "Number of 1 Bits", 191, "Bit Manipulation",
            "Given a positive integer n, return the number of set bits (1s) in its binary representation (its Hamming weight).",
            ["1 ≤ n ≤ 2³¹ − 1"],
            [P("n", ArgKind.Int, "11")],
            [E("3", "11 is 1011 in binary.", "11"), E("1", "128 is 10000000.", "128"), E("30", null, "2147483645")],
            [["1"]],
            Sig("HammingWeight", "int HammingWeight(int n)", global::AlgoSharp.Playground.Sig.Int),
            Starter("public int HammingWeight(int n)", "return 0;"),
            ["n & 1 tells you whether the lowest bit is set; n >> 1 shifts the next bit down.", "Loop until n is 0, adding n & 1 each time.", "Trick: n & (n − 1) clears the lowest set bit, so the loop runs once per 1 bit."],
            a => V(BitOperations.PopCount((uint)a[0].GetInt32())))
        { Validate = a => a[0].GetInt32() >= 1 ? null : "n must be positive.", RandomCase = r => [r.Next(1, 1000).ToString()] },

        new("counting-bits", "Counting Bits", 338, "Bit Manipulation",
            "Given an integer n, return an array ans of length n + 1 such that for each i (0 ≤ i ≤ n), ans[i] is the number of 1s in the binary representation of i.",
            ["0 ≤ n ≤ 32 here"],
            [P("n", ArgKind.Int, "5")],
            [E("[0, 1, 1]", "0 → 0, 1 → 1, 2 → 10.", "2"), E("[0, 1, 1, 2, 1, 2]", null, "5")],
            [["0"]],
            Sig("CountBits", "int[] CountBits(int n)", global::AlgoSharp.Playground.Sig.Int),
            Starter("public int[] CountBits(int n)", "return new int[n + 1];"),
            ["Counting the bits of each number separately is O(n log n).", "i >> 1 is i with its last bit removed, and you've already computed it.", "ans[i] = ans[i >> 1] + (i & 1)."],
            a => V(Enumerable.Range(0, a[0].GetInt32() + 1).Select(i => BitOperations.PopCount((uint)i))))
        { Validate = a => a[0].GetInt32() is >= 0 and <= 32 ? null : "n must be between 0 and 32.", RandomCase = r => [r.Next(0, 17).ToString()] },

        new("reverse-bits", "Reverse Bits", 190, "Bit Manipulation",
            "Reverse the bits of a given 32-bit unsigned integer n and return the result.",
            ["n is a 32-bit unsigned integer"],
            [P("n", ArgKind.UInt, "43261596")],
            [E("964176192", "00000010100101000001111010011100 reversed is 00111001011110000010100101000000.", "43261596"), E("3221225471", null, "4294967293")],
            [["0"], ["1"]],
            Sig("reverseBits", "uint reverseBits(uint n)", global::AlgoSharp.Playground.Sig.UInt),
            Starter("public uint reverseBits(uint n)", "return 0;"),
            ["Process 32 bits, one at a time.", "Each step: shift the result left by 1, add the lowest bit of n (n & 1), then shift n right by 1.", "Watch the variables in binary terms: after 32 steps every bit has moved to its mirrored position."],
            a => V(ReverseBits(a[0].GetUInt32()))),

        new("missing-number", "Missing Number", 268, "Bit Manipulation",
            "Given an array nums containing n distinct numbers in the range [0, n], return the only number in the range that is missing from the array.",
            ["1 ≤ n ≤ 20 here", "All numbers are distinct and in [0, n]"],
            [P("nums", ArgKind.IntArray, "[3, 0, 1]")],
            [E("2", "n = 3, and 2 is missing from [0, 3].", "[3, 0, 1]"), E("2", null, "[0, 1]"), E("8", null, "[9, 6, 4, 2, 3, 5, 7, 0, 1]")],
            [["[1]"], ["[0]"]],
            Sig("MissingNumber", "int MissingNumber(int[] nums)", global::AlgoSharp.Playground.Sig.IntArray),
            Starter("public int MissingNumber(int[] nums)", "return 0;"),
            ["A HashSet of the numbers, then checking 0..n, works with O(n) memory.", "The sum 0 + 1 + … + n is n(n + 1)/2. What does subtracting the actual sum leave?", "XOR also works: XOR all indices 0..n and all values; everything cancels except the missing number."],
            a => V(Enumerable.Range(0, a[0].GetArrayLength() + 1).Except(Ints(a[0])).First()))
        { Validate = a => Ints(a[0]) is { Length: > 0 } n && n.Distinct().Count() == n.Length && n.All(x => x >= 0 && x <= n.Length) ? null : "Use distinct numbers from 0 to n, where n is the array length." },
    ];

    public static PlaygroundProblem? Find(string slug) => All.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private static bool IsSorted(int[] a) => a.Zip(a.Skip(1)).All(p => p.First <= p.Second);

    private static System.Text.Json.Nodes.JsonNode TwoSumAnswer(int[] nums, int target)
    {
        for (int i = 0; i < nums.Length; i++)
            for (int j = i + 1; j < nums.Length; j++)
                if (nums[i] + nums[j] == target) return V(new[] { i, j });
        return V(Array.Empty<int>());
    }

    /// <summary>Any valid pair is accepted (there can be several); [] is correct only when no pair exists.</summary>
    private static Verdict CheckTwoSum(JsonElement[] a, string? resultJson)
    {
        var nums = Ints(a[0]);
        int target = a[1].GetInt32();
        string expected = TwoSumAnswer(nums, target).ToJsonString();
        bool exists = expected != "[]";
        int[]? result;
        try
        {
            result = resultJson is null ? null : JsonSerializer.Deserialize<int[]>(resultJson);
        }
        catch (JsonException)
        {
            return new(false, "The result isn't an int[].", expected);
        }
        if (result is null) return new(false, "Returned null. Return an empty array when there is no pair.", expected);
        if (result.Length == 0)
            return exists ? new(false, "Returned [], but a pair does exist.", expected) : new(true, "Correctly returned [] — no pair adds up to the target.", expected);
        if (result.Length != 2) return new(false, $"Expected two indices, got {result.Length}.", expected);
        int x = result[0], y = result[1];
        if (x < 0 || y < 0 || x >= nums.Length || y >= nums.Length) return new(false, $"Index out of range in [{x}, {y}].", expected);
        if (x == y) return new(false, $"Used index {x} twice; the two elements must be different.", expected);
        return nums[x] + nums[y] == target
            ? new(true, $"nums[{x}] + nums[{y}] = {nums[x]} + {nums[y]} = {target}. Any valid pair is accepted.", expected)
            : new(false, $"nums[{x}] + nums[{y}] = {nums[x] + nums[y]}, not {target}.", expected);
    }
}
