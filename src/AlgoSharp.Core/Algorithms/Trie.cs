using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Prefix tree over lowercase words.</summary>
public sealed class Trie
{
    public sealed class Node
    {
        public readonly SortedDictionary<char, Node> Children = [];
        public bool IsWord;
    }

    private readonly Node root = new();

    public Trie(IEnumerable<string> words)
    {
        T = new GraphTracer(Scene);
        foreach (var w in words) Insert(w);
        T.BeginOperation();
    }

    public GraphTracer T { get; }

    public List<string> Words()
    {
        var result = new List<string>();
        void Walk(Node n, string prefix)
        {
            if (n.IsWord) result.Add(prefix);
            foreach (var (ch, child) in n.Children) Walk(child, prefix + ch);
        }
        Walk(root, "");
        return result;
    }

    public int NodeCount()
    {
        int Count(Node n) => 1 + n.Children.Values.Sum(Count);
        return Count(root);
    }

    private void Scene(SceneBuilder sb)
    {
        var letter = new Dictionary<Node, string>(ReferenceEqualityComparer.Instance) { [root] = "·" };
        var word = new Dictionary<Node, string>(ReferenceEqualityComparer.Instance) { [root] = "" };
        void Name(Node n)
        {
            foreach (var (ch, child) in n.Children)
            {
                letter[child] = ch.ToString();
                word[child] = word[n] + ch;
                Name(child);
            }
        }
        Name(root);
        TreeLayout.Nary(sb, [root], n => n.Children.Values, n => letter[n], n => n.IsWord ? word[n] : null, levelGap: 11);
    }

    // <code id="trie-insert">
    public void Insert(string word)
    {
        Node cur = root; // #root
        T.At("root").Hl(Key, cur).Ptr("cur", cur).V("word", word).Say($"Insert \"{word}\": start at the root. Each edge spells one letter.");
        foreach (char ch in word) // #loop
        {
            T.At("missing").Hl(Key, cur).Ptr("cur", cur).V("ch", ch).Say(cur.Children.ContainsKey(ch) ? $"'{ch}' already has a child: share it (common prefix)." : $"No child for '{ch}' yet.");
            if (!cur.Children.TryGetValue(ch, out Node? next)) // #missing
            {
                next = new Node(); // #new
                cur.Children[ch] = next; // #link
                T.At("link").Hl(Key, cur).Hl(New, next).HlEdge(Active, next).Ptr("cur", cur).V("ch", ch).Say($"Create a node for '{ch}' and link it.");
            }
            cur = next; // #advance
            T.At("advance").Hl(Active, cur).Ptr("cur", cur).V("ch", ch).Say($"Move down to '{ch}'.");
        }
        cur.IsWord = true; // #mark
        T.At("mark").Hl(Found, cur).Ptr("cur", cur).V("word", word).Say($"Mark this node as the end of a word. Insert costs O(length) = O({word.Length}), regardless of how many words are stored.");
    }
    // </code>

    // <code id="trie-search">
    public bool Contains(string word)
    {
        Node? node = Walk(word); // #walkWord
        T.At("isWord").Hl(node is not null && node.IsWord ? Found : Compare, node).Ptr("node", node).V("word", word).V("found path", node is not null).V("IsWord", node?.IsWord ?? false).Say(node is null ? $"The path for \"{word}\" breaks off: not stored." : node.IsWord ? $"The path exists and ends at a word marker: \"{word}\" is stored." : $"The path exists, but \"{word}\" is only a prefix of longer words.");
        return node != null && node.IsWord; // #isWord
    }

    public bool StartsWith(string prefix)
    {
        Node? node = Walk(prefix); // #walkPrefix
        T.At("hasPrefix").Hl(node is null ? Compare : Found, node).Ptr("node", node).V("prefix", prefix).V("found", node is not null).Say(node is null ? $"No stored word starts with \"{prefix}\"." : $"Every word below this node starts with \"{prefix}\".");
        return node != null; // #hasPrefix
    }

    private Node? Walk(string s)
    {
        Node cur = root; // #walkRoot
        foreach (char ch in s) // #walkLoop
        {
            T.At("walkMissing").Hl(Key, cur).Ptr("cur", cur).V("ch", ch).Say(cur.Children.ContainsKey(ch) ? $"Follow the '{ch}' edge." : $"There is no '{ch}' edge here.");
            if (!cur.Children.TryGetValue(ch, out Node? next)) // #walkMissing
            {
                return null; // #walkNull
            }
            T.Mark(Visited, cur);
            cur = next; // #walkAdvance
        }
        return cur; // #walkRet
    }
    // </code>
}
