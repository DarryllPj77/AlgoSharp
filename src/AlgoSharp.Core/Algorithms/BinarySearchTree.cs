using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Unbalanced binary search tree with unique integer keys.</summary>
public sealed class BinarySearchTree
{
    public sealed class Node
    {
        public int Key;
        public Node? Left;
        public Node? Right;

        public Node(int key) => Key = key;
    }

    private Node? root;

    public BinarySearchTree(IEnumerable<int> keys)
    {
        T = new GraphTracer(sb => TreeLayout.Binary(sb, root, n => n.Left, n => n.Right, n => n.Key.ToString()));
        foreach (int k in keys) Insert(k);
        T.BeginOperation();
    }

    public GraphTracer T { get; }

    public int Count => Keys().Count;

    public int Height()
    {
        int H(Node? n) => n is null ? 0 : 1 + Math.Max(H(n.Left), H(n.Right));
        return H(root);
    }

    public List<int> Keys()
    {
        var result = new List<int>();
        void Walk(Node? n)
        {
            if (n is null) return;
            Walk(n.Left);
            result.Add(n.Key);
            Walk(n.Right);
        }
        Walk(root);
        return result;
    }

    // <code id="bst-insert">
    public bool Insert(int key)
    {
        if (root == null) // #empty
        {
            root = new Node(key); // #root
            T.At("root").Hl(New, root).Ptr("root", root).V("key", key).Say($"The tree is empty, so {key} becomes the root.");
            return true; // #retRoot
        }
        Node cur = root; // #start
        T.At("start").Hl(Active, cur).Ptr("cur", cur).V("key", key).Say($"Start at the root and look for the empty spot where {key} belongs.");
        while (true) // #loop
        {
            T.At("dup").Hl(Compare, cur).Ptr("cur", cur).V("key", key).V("cur.Key", cur.Key).Say(key == cur.Key ? $"{key} equals {cur.Key}." : key < cur.Key ? $"{key} < {cur.Key}: go left." : $"{key} > {cur.Key}: go right.");
            if (key == cur.Key) // #dup
            {
                T.At("dupRet").Hl(Found, cur).Ptr("cur", cur).V("key", key).Say($"{key} is already in the tree. This BST stores unique keys, so nothing changes.");
                return false; // #dupRet
            }
            if (key < cur.Key) // #lt
            {
                if (cur.Left == null) // #leftNull
                {
                    cur.Left = new Node(key); // #linkLeft
                    T.At("linkLeft").Hl(Active, cur).Hl(New, cur.Left).HlEdge(Active, cur.Left).Ptr("cur", cur).V("key", key).Say($"{cur.Key} has no left child: attach {key} there.");
                    return true; // #retLeft
                }
                cur = cur.Left; // #goLeft
            }
            else
            {
                if (cur.Right == null) // #rightNull
                {
                    cur.Right = new Node(key); // #linkRight
                    T.At("linkRight").Hl(Active, cur).Hl(New, cur.Right).HlEdge(Active, cur.Right).Ptr("cur", cur).V("key", key).Say($"{cur.Key} has no right child: attach {key} there.");
                    return true; // #retRight
                }
                cur = cur.Right; // #goRight
            }
        }
    }
    // </code>

    // <code id="bst-search">
    public bool Contains(int key)
    {
        Node? cur = root; // #start
        while (cur != null) // #loop
        {
            T.At("eq").Hl(Compare, cur).Ptr("cur", cur).V("key", key).V("cur.Key", cur.Key).Say(key == cur.Key ? $"{key} equals {cur.Key}." : key < cur.Key ? $"{key} < {cur.Key}: the key can only be in the left subtree." : $"{key} > {cur.Key}: the key can only be in the right subtree.");
            if (key == cur.Key) // #eq
            {
                T.At("found").Hl(Found, cur).Ptr("cur", cur).V("key", key).Say($"Found {key}. Each comparison skipped a whole subtree.");
                return true; // #found
            }
            T.Mark(Visited, cur);
            T.At("step").Hl(Active, key < cur.Key ? cur.Left : cur.Right).HlEdge(Active, key < cur.Key ? cur.Left : cur.Right).Ptr("cur", key < cur.Key ? cur.Left : cur.Right).V("key", key).Say((key < cur.Key ? cur.Left : cur.Right) is null ? $"The {(key < cur.Key ? "left" : "right")} child of {cur.Key} is null." : $"Move {(key < cur.Key ? "left" : "right")} to {(key < cur.Key ? cur.Left : cur.Right)!.Key}.");
            cur = key < cur.Key ? cur.Left : cur.Right; // #step
        }
        T.At("notfound").V("key", key).Say($"Fell off the tree: {key} is not present.");
        return false; // #notfound
    }
    // </code>

    // <code id="bst-delete">
    public void Delete(int key)
    {
        root = Delete(root, key, T); // #start
        T.At("start").V("key", key).Say($"Delete({key}) finished. In-order keys: [{string.Join(", ", Keys())}].");
    }

    private Node? Delete(Node? node, int key, GraphTracer T)
    {
        if (node == null) // #null
        {
            T.At("null").V("key", key).Say($"Reached an empty subtree: {key} is not in the tree.");
            return null; // #retNull
        }
        T.At("lt").Hl(Compare, node).Ptr("node", node).V("key", key).V("node.Key", node.Key).Say(key < node.Key ? $"{key} < {node.Key}: delete from the left subtree." : key > node.Key ? $"{key} > {node.Key}: delete from the right subtree." : $"Found {key}: this is the node to delete.");
        if (key < node.Key) // #lt
        {
            node.Left = Delete(node.Left, key, T); // #goLeft
        }
        else if (key > node.Key) // #gt
        {
            node.Right = Delete(node.Right, key, T); // #goRight
        }
        else
        {
            if (node.Left == null) // #noLeft
            {
                T.At("retRight").Hl(Removed, node).Hl(Active, node.Right).Ptr("node", node).V("key", key).Say(node.Right is null ? $"{node.Key} is a leaf: return null so its parent drops it." : $"{node.Key} has only a right child: return it to take {node.Key}'s place.");
                return node.Right; // #retRight
            }
            if (node.Right == null) // #noRight
            {
                T.At("retLeft").Hl(Removed, node).Hl(Active, node.Left).Ptr("node", node).V("key", key).Say($"{node.Key} has only a left child: return it to take {node.Key}'s place.");
                return node.Left; // #retLeft
            }
            Node successor = node.Right; // #succ
            T.At("succ").Hl(Removed, node).Hl(Key, successor).Ptr("node", node).Ptr("succ", successor).V("key", key).Say($"{node.Key} has two children. Its replacement is the in-order successor: the smallest key in the right subtree.");
            while (successor.Left != null) // #succLoop
            {
                successor = successor.Left; // #succWalk
                T.At("succWalk").Hl(Removed, node).Hl(Key, successor).HlEdge(Active, successor).Ptr("node", node).Ptr("succ", successor).V("key", key).Say($"Keep going left: {successor.Key}.");
            }
            node.Key = successor.Key; // #copy
            T.At("copy").Hl(New, node).Hl(Key, successor).Ptr("node", node).Ptr("succ", successor).V("node.Key", node.Key).Say($"Copy the successor's key {successor.Key} into the node. Now remove the duplicate {successor.Key} from the right subtree.");
            node.Right = Delete(node.Right, successor.Key, T); // #delSucc
        }
        return node; // #ret
    }
    // </code>

    // <code id="bst-inorder">
    public List<int> InOrder()
    {
        var result = new List<int>(); // #list
        InOrder(root, result, T); // #start
        T.At("ret").V("result", result.ToArray()).Say($"In-order traversal of a BST visits keys in sorted order: [{string.Join(", ", result)}].");
        return result; // #ret
    }

    private static void InOrder(Node? node, List<int> result, GraphTracer T)
    {
        if (node == null) return; // #null
        T.At("left").Hl(Active, node).Ptr("node", node).V("result", result.ToArray()).Say($"At {node.Key}: first traverse its left subtree.");
        InOrder(node.Left, result, T); // #left
        result.Add(node.Key); // #visit
        T.Mark(Visited, node);
        T.At("visit").Hl(Found, node).Ptr("node", node).V("result", result.ToArray()).Say($"Left side done: visit {node.Key}.");
        InOrder(node.Right, result, T); // #right
    }
    // </code>
}
