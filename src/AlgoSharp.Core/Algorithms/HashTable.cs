using AlgoSharp.Core.Engine;
using static AlgoSharp.Core.Engine.CellState;

namespace AlgoSharp.Core.Algorithms;

/// <summary>Hash set of non-negative integers using separate chaining.</summary>
public sealed class HashTable
{
    public sealed class Entry
    {
        public int Key;
        public Entry? Next;

        public Entry(int key, Entry? next)
        {
            Key = key;
            Next = next;
        }
    }

    /// <summary>Identity of a bucket slot in the visualization.</summary>
    public readonly record struct Bucket(int Index);

    private readonly Entry?[] buckets;
    private int count;

    public HashTable(int size, IEnumerable<int> keys)
    {
        buckets = new Entry?[size];
        T = new GraphTracer(Scene);
        foreach (int k in keys) Insert(k);
        T.BeginOperation();
    }

    public GraphTracer T { get; }
    public int Count => count;
    public int BucketCount => buckets.Length;

    public List<int> Chain(int index)
    {
        var result = new List<int>();
        for (var e = buckets[index]; e != null; e = e.Next) result.Add(e.Key);
        return result;
    }

    private void Scene(SceneBuilder sb)
    {
        const double rowGap = 8.5, top = 6;
        for (int b = 0; b < buckets.Length; b++)
        {
            double y = top + b * rowGap;
            var bucket = new Bucket(b);
            sb.Node(bucket, b.ToString(), 7, y, NodeShape.Slot);
            object prev = bucket;
            int k = 0;
            for (var e = buckets[b]; e != null && k < 8; e = e.Next, k++)
            {
                sb.Node(e, e.Key.ToString(), 22 + k * 11, y);
                sb.Edge(prev, prev, e, directed: true);
                prev = e;
            }
        }
        sb.Height = top + (buckets.Length - 1) * rowGap + 6;
    }

    // <code id="hash-insert">
    private int Hash(int key) => key % buckets.Length; // #hashFn

    public bool Insert(int key)
    {
        int index = Hash(key); // #hash
        T.At("hash").Hl(Key, new Bucket(index)).V("key", key).V("index", index).V("count", count).Say($"hash({key}) = {key} % {buckets.Length} = {index}. Go straight to bucket {index}.");
        for (Entry? e = buckets[index]; e != null; e = e.Next) // #scan
        {
            T.At("cmp").Hl(Key, new Bucket(index)).Hl(Compare, e).Ptr("e", e).V("key", key).V("e.Key", e.Key).Say(e.Key == key ? $"{e.Key} equals {key}." : $"{e.Key} ≠ {key}: keep walking the chain.");
            if (e.Key == key) // #cmp
            {
                T.At("dup").Hl(Found, e).Ptr("e", e).V("key", key).Say($"{key} is already stored. A set keeps keys unique, so nothing changes.");
                return false; // #dup
            }
        }
        buckets[index] = new Entry(key, buckets[index]); // #prepend
        count++; // #count
        T.At("prepend").Hl(Key, new Bucket(index)).Hl(New, buckets[index]).HlEdge(Active, new Bucket(index)).V("key", key).V("count", count).V("load factor", Math.Round((double)count / buckets.Length, 2)).Say($"Not found, so insert {key} at the front of bucket {index}'s chain — O(1).");
        return true; // #ret
    }
    // </code>

    // <code id="hash-search">
    public bool Contains(int key)
    {
        int index = Hash(key); // #hash
        T.At("hash").Hl(Key, new Bucket(index)).V("key", key).V("index", index).Say($"hash({key}) = {key} % {buckets.Length} = {index}. Only bucket {index} can contain it.");
        for (Entry? e = buckets[index]; e != null; e = e.Next) // #scan
        {
            T.At("cmp").Hl(Key, new Bucket(index)).Hl(Compare, e).Ptr("e", e).V("key", key).V("e.Key", e.Key).Say(e.Key == key ? $"{e.Key} equals {key}." : $"{e.Key} ≠ {key}.");
            if (e.Key == key) // #cmp
            {
                T.At("found").Hl(Found, e).Ptr("e", e).V("key", key).Say($"Found {key} in bucket {index}. With short chains, lookup is O(1) on average.");
                return true; // #found
            }
        }
        T.At("notfound").Hl(Key, new Bucket(index)).V("key", key).Say($"End of the chain: {key} is not in the table.");
        return false; // #notfound
    }
    // </code>

    // <code id="hash-delete">
    public bool Remove(int key)
    {
        int index = Hash(key); // #hash
        T.At("hash").Hl(Key, new Bucket(index)).V("key", key).V("index", index).Say($"hash({key}) = {key} % {buckets.Length} = {index}.");
        Entry? prev = null; // #prev
        Entry? e = buckets[index]; // #start
        while (e != null) // #loop
        {
            T.At("cmp").Hl(Key, new Bucket(index)).Hl(Compare, e).Ptr("e", e).Ptr("prev", prev).V("key", key).V("e.Key", e.Key).Say(e.Key == key ? $"{e.Key} equals {key}: unlink it." : $"{e.Key} ≠ {key}: move along.");
            if (e.Key == key) // #cmp
            {
                T.At("cmp").Hl(Removed, e).Ptr("e", e).Ptr("prev", prev).V("key", key).Say(prev is null ? "It's the first entry, so the bucket must point past it." : $"Make {prev.Key}.Next skip over it.");
                if (prev == null) // #isFirst
                {
                    buckets[index] = e.Next; // #unlinkHead
                    T.At("unlinkHead").Hl(Key, new Bucket(index)).HlEdge(Active, new Bucket(index)).V("key", key).Say($"Bucket {index} now points to {(e.Next is null ? "nothing" : e.Next.Key.ToString())}.");
                }
                else
                {
                    prev.Next = e.Next; // #unlink
                    T.At("unlink").Hl(Active, prev).HlEdge(Active, prev).Ptr("prev", prev).V("key", key).Say($"{prev.Key}.Next now skips the removed entry.");
                }
                count--; // #count
                T.At("count").V("count", count).V("load factor", Math.Round((double)count / buckets.Length, 2)).Say($"{key} removed.");
                return true; // #ret
            }
            prev = e; // #advance
            e = e.Next; // #next
        }
        T.At("notfound").Hl(Key, new Bucket(index)).V("key", key).Say($"{key} is not in the table: nothing to remove.");
        return false; // #notfound
    }
    // </code>
}
