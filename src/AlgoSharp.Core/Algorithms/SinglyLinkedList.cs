using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

public sealed class SinglyLinkedList
{
    // <code id="list-node">
    public sealed class Node
    {
        public int Value;
        public Node? Next;

        public Node(int value) => Value = value;
    }
    // </code>

    private Node? head;
    private int count;

    public SinglyLinkedList(IEnumerable<int> values)
    {
        T = new ListTracer<Node>(() => head, n => n.Next, n => n.Value);
        Node? tail = null;
        foreach (int v in values)
        {
            var node = new Node(v);
            if (tail is null) head = node; else tail.Next = node;
            tail = node;
            count++;
        }
    }

    public ListTracer<Node> T { get; }
    public int Count => count;

    public int[] ToArray()
    {
        var result = new List<int>();
        for (var n = head; n is not null; n = n.Next) result.Add(n.Value);
        return [.. result];
    }

    // <code id="list-insert">
    public void InsertAt(int index, int value)
    {
        if (index < 0 || index > count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error($"Index {index} is invalid: you can insert at 0..{count}.");
            throw new IndexOutOfRangeException(); // #throw
        }
        Node node = new Node(value); // #new
        T.Fresh(node);
        T.At("new").Ptr("node", node).V("index", index).V("value", value).Say($"Allocate a new node holding {value}. It is not linked to anything yet.");
        if (index == 0) // #isHead
        {
            node.Next = head; // #linkHead
            T.At("linkHead").Hl(New, node).HlLink(Active, node).Ptr("node", node).V("index", index).Say("Inserting at the front: point the new node at the current head.");
            head = node; // #setHead
            T.At("setHead").Hl(New, node).Ptr("node", node).V("index", index).Say("Make head point to the new node. No other node moved — O(1).");
        }
        else
        {
            Node prev = head!; // #prevInit
            T.At("prevInit").Hl(Active, prev).Ptr("node", node).Ptr("prev", prev).V("index", index).V("i", 0).Say($"Start at head. We need the node just before position {index}.");
            for (int i = 0; i < index - 1; i++) // #walk
            {
                prev = prev.Next!; // #advance
                T.At("advance").Hl(Active, prev).Ptr("node", node).Ptr("prev", prev).V("index", index).V("i", i + 1).Say($"Follow the Next pointer: prev moves to {prev.Value}.");
            }
            node.Next = prev.Next; // #linkNext
            T.At("linkNext").Hl(Active, prev).Hl(New, node).HlLink(Active, node).Ptr("node", node).Ptr("prev", prev).V("index", index).Say(node.Next is null ? "node.Next = prev.Next, which is null — the new node will be the last one." : $"First, point the new node at prev's successor ({node.Next.Value}). Order matters: doing this second would lose the rest of the list!");
            prev.Next = node; // #linkPrev
            T.At("linkPrev").Hl(Active, prev).Hl(New, node).HlLink(Active, prev).Ptr("node", node).Ptr("prev", prev).V("index", index).Say($"Then redirect prev.Next to the new node. {value} is now part of the chain.");
        }
        count++; // #count
        T.At("count").Hl(New, node).V("count", count).Say($"count = {count}.");
    }
    // </code>

    // <code id="list-delete">
    public int RemoveAt(int index)
    {
        if (index < 0 || index >= count) // #check
        {
            T.At("throw").V("index", index).V("count", count).Error(count == 0 ? "The list is empty." : $"Index {index} is outside 0..{count - 1}.");
            throw new IndexOutOfRangeException(); // #throw
        }
        Node removed;
        if (index == 0) // #isHead
        {
            removed = head!; // #takeHead
            T.At("takeHead").Hl(Removed, removed).Ptr("removed", removed).V("index", index).Say($"Deleting the first node ({removed.Value}).");
            head = head!.Next; // #moveHead
            T.At("moveHead").Hl(Removed, removed).Ptr("removed", removed).V("index", index).Say("Move head forward to the second node. The old first node is no longer reachable.");
        }
        else
        {
            Node prev = head!; // #prevInit
            T.At("prevInit").Hl(Active, prev).Ptr("prev", prev).V("index", index).V("i", 0).Say($"Start at head and walk to the node before position {index}.");
            for (int i = 0; i < index - 1; i++) // #walk
            {
                prev = prev.Next!; // #advance
                T.At("advance").Hl(Active, prev).Ptr("prev", prev).V("index", index).V("i", i + 1).Say($"prev moves to {prev.Value}.");
            }
            removed = prev.Next!; // #target
            T.At("target").Hl(Active, prev).Hl(Removed, removed).Ptr("prev", prev).Ptr("removed", removed).V("index", index).Say($"The node to delete is prev.Next ({removed.Value}).");
            prev.Next = removed.Next; // #bypass
            T.At("bypass").Hl(Active, prev).Hl(Removed, removed).HlLink(Active, prev).Ptr("prev", prev).Ptr("removed", removed).V("index", index).Say(prev.Next is null ? "Bypass it: prev.Next becomes null, so prev is the new last node." : $"Bypass it: prev.Next now skips straight to {prev.Next.Value}.");
        }
        removed.Next = null; // #detach
        T.At("detach").Hl(Removed, removed).Ptr("removed", removed).V("removed", removed.Value).Say("Clear the removed node's Next so it holds no references. The garbage collector will reclaim it.");
        count--; // #count
        T.At("count").V("count", count).V("removed", removed.Value).Say($"count = {count}. Return {removed.Value}.");
        return removed.Value; // #return
    }
    // </code>

    // <code id="list-search">
    public int IndexOf(int value)
    {
        Node? cur = head; // #init
        int index = 0; // #index
        T.At("init").Hl(Active, cur).Ptr("cur", cur).V("index", index).V("value", value).Say($"Start at head. A linked list has no indices, so we must walk node by node to find {value}.");
        while (cur != null) // #loop
        {
            T.At("cmp").Hl(Compare, cur).Ptr("cur", cur).V("index", index).V("cur.Value", cur.Value).V("value", value).Say(cur.Value == value ? $"cur.Value = {cur.Value} matches!" : $"cur.Value = {cur.Value} is not {value}.");
            if (cur.Value == value) // #cmp
            {
                T.At("found").Hl(Found, cur).Ptr("cur", cur).V("index", index).Say($"Found {value} at position {index}.");
                return index; // #found
            }
            cur = cur.Next; // #next
            index++; // #inc
            T.At("next").Hl(Active, cur).Ptr("cur", cur).V("index", index).Say(cur is null ? "cur.Next was null — we fell off the end." : $"Follow Next to {cur.Value}.");
        }
        T.At("notfound").Ptr("cur", cur).V("index", index).Say($"{value} is not in the list. Return -1.");
        return -1; // #notfound
    }
    // </code>

    // <code id="list-traverse">
    public int Sum()
    {
        int sum = 0; // #sum
        T.At("sum").V("sum", sum).Say("Traverse the whole list, adding up every value.");
        for (Node? cur = head; cur != null; cur = cur.Next) // #loop
        {
            sum += cur.Value; // #add
            T.At("add").Hl(Visited, cur).HlLink(Active, cur).Ptr("cur", cur).V("cur.Value", cur.Value).V("sum", sum).Say($"Visit {cur.Value}: sum = {sum}.");
        }
        T.At("ret").V("sum", sum).Say($"cur became null — every node was visited once. O(n). Sum = {sum}.");
        return sum; // #ret
    }
    // </code>
}
