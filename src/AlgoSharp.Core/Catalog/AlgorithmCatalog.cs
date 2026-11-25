using AlgoSharp.Core.Algorithms;

namespace AlgoSharp.Core.Catalog;

public static class AlgorithmCatalog
{
    public static readonly Category Arrays = new("arrays", "Arrays", "Contiguous memory with O(1) indexing.");
    public static readonly Category Sorting = new("sorting", "Sorting", "Putting values in order, many ways.");
    public static readonly Category Searching = new("searching", "Searching", "Finding a value, fast or thorough.");
    public static readonly Category LinkedLists = new("lists", "Linked Lists", "Nodes connected by references.");
    public static readonly Category StacksQueues = new("stacks", "Stacks & Queues", "Restricted access: LIFO and FIFO.");
    public static readonly Category Trees = new("trees", "Trees & Heaps", "Hierarchies for fast lookup and priority.");
    public static readonly Category Graphs = new("graphs", "Graphs", "Networks of vertices and edges.");
    public static readonly Category Advanced = new("advanced", "Advanced", "Specialised structures and techniques.");

    public static IReadOnlyList<Category> Categories { get; } =
        [Arrays, Sorting, Searching, LinkedLists, StacksQueues, Trees, Graphs, Advanced];

    private static readonly ParamDef Index = new("index", "Index", 2, 0, 20);
    private static readonly ParamDef Value = new("value", "Value", 42, -99, 999);
    private static readonly ParamDef Target = new("target", "Target", 42, -99, 999);
    private static readonly ParamDef Start = new("start", "Start vertex", 0, 0, Graph.MaxVertices - 1, ParamKind.Vertex);

    private static readonly InputSpec GraphInput = new(0, 0, 0, 0, []) { Kind = InputKind.Graph, DefaultText = Graph.DefaultText };

    private static OperationDef Sort(string codeId, string summary, Complexity c) =>
        new("sort", "Sort", codeId, summary, c, []);

    private static readonly InputSpec SortInput = new(2, 16, 1, 99, [38, 27, 43, 3, 9, 82, 10, 55, 19]);

    public static IReadOnlyList<AlgorithmDef> All { get; } =
    [
        new("array", "Array", Arrays, Difficulty.Beginner,
            "Access, insert, delete, update and search in a fixed-capacity array.",
            "An array stores values side by side in memory. Because every slot has the same size, the address of slot i is simply start + i × size, which makes reading or writing any index instant. The trade-off: inserting or deleting in the middle forces every later value to shift.",
            ["Indexing is O(1): the address is computed, not searched.", "Insert/delete in the middle shift elements: O(n).", "This class keeps a count of used slots, exactly like List<T> does internally."],
            [
                new("access", "Access", "array-access", "Read the value at an index.", new("O(1)", "O(1)", "O(1)", "O(1)", "Direct address arithmetic."), [Index]),
                new("insert", "Insert", "array-insert", "Insert a value at an index, shifting later values right.", new("O(1)", "O(n)", "O(n)", "O(1)", "Best case: inserting at the end."), [Index, Value]),
                new("delete", "Delete", "array-delete", "Remove the value at an index, shifting later values left.", new("O(1)", "O(n)", "O(n)", "O(1)", "Best case: deleting the last value."), [Index]),
                new("update", "Update", "array-update", "Overwrite the value at an index.", new("O(1)", "O(1)", "O(1)", "O(1)", "No shifting needed."), [Index, Value]),
                new("search", "Linear Search", "array-search", "Scan from the start until the value is found.", new("O(1)", "O(n)", "O(n)", "O(1)", "Unsorted data: every slot may need checking."), [Value]),
            ],
            new(0, 11, 0, 99, [12, 45, 7, 23, 56, 89]),
            "array",
            v => new ArraySession(v, 12)),

        new("bubble-sort", "Bubble Sort", Sorting, Difficulty.Beginner,
            "Repeatedly swap neighbours that are out of order.",
            "Bubble sort walks through the array comparing each pair of neighbours and swapping them when they are in the wrong order. After each pass the largest remaining value has 'bubbled' to the end, so the unsorted region shrinks by one. If a pass makes no swaps, the array is sorted and we can stop early.",
            ["After pass i, the last i values are final.", "The swapped flag gives an O(n) best case on sorted input.", "Stable: equal values keep their order."],
            [Sort("bubble-sort", "Sort the array with bubble sort.", new("O(n)", "O(n²)", "O(n²)", "O(1)", "Best case needs the early-exit flag and already sorted input."))],
            SortInput, "bubble", v => new SortSession(v, Algorithms.Sorting.BubbleSort)),

        new("selection-sort", "Selection Sort", Sorting, Difficulty.Beginner,
            "Select the minimum and move it to the front.",
            "Selection sort grows a sorted prefix one value at a time. On each pass it scans the unsorted part for the smallest value and swaps it into the next position of the prefix. It always does the same number of comparisons, no matter how the input looks.",
            ["Exactly n−1 swaps at most — useful when writes are expensive.", "Always O(n²) comparisons, even on sorted input.", "Not stable in this swap-based form."],
            [Sort("selection-sort", "Sort the array with selection sort.", new("O(n²)", "O(n²)", "O(n²)", "O(1)", "Comparisons don't depend on input order."))],
            SortInput, "selection", v => new SortSession(v, Algorithms.Sorting.SelectionSort)),

        new("insertion-sort", "Insertion Sort", Sorting, Difficulty.Beginner,
            "Insert each value into the sorted part on its left.",
            "Insertion sort works like sorting playing cards in your hand. It picks up the next value (the key), shifts every larger value in the sorted part one place to the right, and drops the key into the gap. It is very fast on small or nearly-sorted inputs.",
            ["Excellent for nearly sorted data: O(n) best case.", "Shifts instead of swaps: one write per moved value.", "Stable and in-place; used inside hybrid sorts like Introsort."],
            [Sort("insertion-sort", "Sort the array with insertion sort.", new("O(n)", "O(n²)", "O(n²)", "O(1)", "Best case: input already sorted."))],
            SortInput, "insertion", v => new SortSession(v, Algorithms.Sorting.InsertionSort)),

        new("merge-sort", "Merge Sort", Sorting, Difficulty.Intermediate,
            "Divide, sort the halves recursively, then merge.",
            "Merge sort splits the array in half, sorts each half recursively, and then merges the two sorted halves by repeatedly taking the smaller front value. Splitting stops at single values, which are sorted by definition. Its running time is O(n log n) no matter the input.",
            ["Guaranteed O(n log n): log n levels, O(n) merging per level.", "Needs O(n) extra memory for the temporary halves.", "Stable — Array.Sort on objects in some runtimes uses merge-based sorts for this reason."],
            [Sort("merge-sort", "Sort the array with merge sort.", new("O(n log n)", "O(n log n)", "O(n log n)", "O(n)", "Temporary arrays hold the halves during merging."))],
            SortInput, "merge", v => new SortSession(v, Algorithms.Sorting.MergeSort)),

        new("quick-sort", "Quick Sort", Sorting, Difficulty.Intermediate,
            "Partition around a pivot, then recurse on both sides.",
            "Quick sort picks a pivot (here: the last value) and partitions the range so that everything ≤ pivot ends up on its left and everything larger on its right. The pivot is then in its final position, and the same process repeats on each side. This uses the Lomuto partition scheme.",
            ["Average O(n log n) with excellent cache behaviour.", "Worst case O(n²) when pivots are always extreme (e.g. sorted input with last-element pivot).", "In-place, but not stable."],
            [Sort("quick-sort", "Sort the array with quick sort.", new("O(n log n)", "O(n log n)", "O(n²)", "O(log n)", "Space is the recursion depth; worst case O(n)."))],
            SortInput, "quick", v => new SortSession(v, Algorithms.Sorting.QuickSort)),

        new("linear-search", "Linear Search", Searching, Difficulty.Beginner,
            "Check every value from left to right.",
            "Linear search compares the target with each value in turn until it finds a match or runs out of values. It works on any array, sorted or not.",
            ["Works on unsorted data.", "O(n) worst case: the target may be last or missing.", "Simple, and often fastest for very small arrays."],
            [new("search", "Search", "linear-search", "Search for a target value.", new("O(1)", "O(n)", "O(n)", "O(1)", "Best case: the first value matches."), [Target with { Default = 55 }])],
            new(1, 16, 0, 99, [24, 7, 91, 33, 55, 12, 68, 40]),
            "scan", v => new SearchSession(v, Algorithms.Searching.LinearSearch)),

        new("binary-search", "Binary Search", Searching, Difficulty.Beginner,
            "Halve a sorted range on every comparison.",
            "Binary search only works on sorted data. It looks at the middle value: if it's the target we're done, if it's too small the target must be in the right half, otherwise in the left half. Each comparison throws away half of the remaining values.",
            ["Requires sorted input.", "O(log n): 1,000,000 values need at most 20 comparisons.", "mid = lo + (hi − lo) / 2 avoids integer overflow on huge arrays."],
            [new("search", "Search", "binary-search", "Search a sorted array for a target.", new("O(1)", "O(log n)", "O(log n)", "O(1)", "Each step halves the search range."), [Target with { Default = 68 }])],
            new(1, 16, 0, 99, [3, 9, 14, 21, 27, 33, 40, 52, 68, 75, 81, 94], SortedRequired: true),
            "search", v => new SearchSession(v, Algorithms.Searching.BinarySearch)),

        new("linked-list", "Singly Linked List", LinkedLists, Difficulty.Beginner,
            "Insert, delete, search and traverse a chain of nodes.",
            "A singly linked list stores each value in a separate node that holds a reference to the next node. There are no indices: to reach position k you follow k Next references from the head. In exchange, inserting or removing a node only rewires a couple of references — nothing is shifted.",
            ["Access by position is O(n): you must walk from head.", "Insert/delete at the head is O(1).", "Rewire pointers in the right order, or you lose the rest of the list!"],
            [
                new("insert", "Insert", "list-insert", "Insert a value at a position.", new("O(1)", "O(n)", "O(n)", "O(1)", "O(1) at the head; otherwise walk to the previous node."), [Index with { Default = 2 }, Value with { Default = 99 }]),
                new("delete", "Delete", "list-delete", "Remove the node at a position.", new("O(1)", "O(n)", "O(n)", "O(1)", "O(1) at the head; otherwise walk to the previous node."), [Index with { Default = 1 }]),
                new("search", "Search", "list-search", "Find the position of a value.", new("O(1)", "O(n)", "O(n)", "O(1)", "Must follow references one by one."), [Value with { Default = 30 }]),
                new("traverse", "Traverse", "list-traverse", "Visit every node (sum all values).", new("O(n)", "O(n)", "O(n)", "O(1)", "Every node is visited exactly once."), []),
            ],
            new(0, 8, 0, 99, [10, 20, 30, 40]),
            "list", v => new LinkedListSession(v)),

        new("stack", "Stack", StacksQueues, Difficulty.Beginner,
            "Push, pop and peek — last in, first out.",
            "A stack only lets you touch its top. Push puts a value on top, pop removes the top value, and peek reads it without removing. This one is backed by an array and a top index, so every operation is O(1). Stacks power undo, the call stack, and expression parsing.",
            ["LIFO: the last value pushed is the first popped.", "All operations are O(1).", "top = -1 means empty; top = capacity − 1 means full."],
            [
                new("push", "Push", "stack-push", "Put a value on top.", new("O(1)", "O(1)", "O(1)", "O(1)", "No other element moves."), [Value with { Default = 7 }]),
                new("pop", "Pop", "stack-pop", "Remove and return the top value.", new("O(1)", "O(1)", "O(1)", "O(1)", "Just move the top index."), []),
                new("peek", "Peek", "stack-peek", "Read the top value without removing it.", new("O(1)", "O(1)", "O(1)", "O(1)", ""), []),
            ],
            new(0, 8, 0, 99, [4, 15, 23]),
            "stack", v => new StackSession(v, 8)),

        new("queue", "Queue", StacksQueues, Difficulty.Beginner,
            "Enqueue, dequeue and peek — first in, first out.",
            "A queue adds at the rear and removes from the front, like a line at a checkout. This implementation uses a circular array: front and rear indices move forward and wrap around to 0, so no element ever has to shift.",
            ["FIFO: the first value enqueued is the first dequeued.", "The circular array makes both ends O(1).", "rear = (rear + 1) % capacity is the wrap-around trick."],
            [
                new("enqueue", "Enqueue", "queue-enqueue", "Add a value at the rear.", new("O(1)", "O(1)", "O(1)", "O(1)", ""), [Value with { Default = 8 }]),
                new("dequeue", "Dequeue", "queue-dequeue", "Remove and return the front value.", new("O(1)", "O(1)", "O(1)", "O(1)", "No shifting thanks to the circular buffer."), []),
                new("peek", "Peek", "queue-peek", "Read the front value without removing it.", new("O(1)", "O(1)", "O(1)", "O(1)", ""), []),
            ],
            new(0, 8, 0, 99, [5, 12, 19, 26]),
            "queue", v => new QueueSession(v, 8)),

        new("bst", "Binary Search Tree", Trees, Difficulty.Intermediate,
            "Insert, search, delete and traverse an ordered tree.",
            "In a binary search tree every node's left subtree holds smaller keys and its right subtree larger keys. Each comparison therefore discards a whole subtree, so a balanced tree answers lookups in O(log n). This tree is not self-balancing: insert keys in sorted order and watch it degrade into a linked list.",
            ["Left < node < right holds at every node.", "Height decides the cost: O(log n) balanced, O(n) degenerate.", "Deleting a node with two children copies in its in-order successor.", "In-order traversal visits keys in sorted order."],
            [
                new("insert", "Insert", "bst-insert", "Walk down from the root and attach a new leaf.", new("O(log n)", "O(log n)", "O(n)", "O(1)", "Iterative, so no recursion stack. Worst case: a degenerate (list-like) tree."), [Value with { Default = 35 }]),
                new("search", "Search", "bst-search", "Follow left/right links by comparing keys.", new("O(1)", "O(log n)", "O(n)", "O(1)", "Best case: the key is at the root."), [Value with { Default = 60 }]),
                new("delete", "Delete", "bst-delete", "Remove a key, handling 0, 1 or 2 children.", new("O(log n)", "O(log n)", "O(n)", "O(h)", "Recursive: space is the tree height h."), [Value with { Default = 30 }]),
                new("inorder", "In-order", "bst-inorder", "Visit left subtree, node, right subtree.", new("O(n)", "O(n)", "O(n)", "O(h)", "Every node is visited once; recursion depth is the height."), []),
            ],
            new(0, 15, 0, 99, [50, 30, 70, 20, 40, 60, 80]),
            "tree", v => new BstSession(v), Phase: 2),

        new("binary-heap", "Binary Heap", Trees, Difficulty.Intermediate,
            "A min-priority queue stored in a plain array.",
            "A binary min-heap is a complete binary tree where every parent is ≤ its children, so the minimum is always at the root. Because the tree is complete, it fits in an array with no pointers: the children of index i are at 2i + 1 and 2i + 2. .NET's PriorityQueue<TElement, TPriority> is built on the same idea.",
            ["Parent ≤ children at every node (heap property).", "Insert: add as the last leaf, then sift up.", "ExtractMin: move the last leaf to the root, then sift down.", "Both views below show the same array."],
            [
                new("insert", "Insert", "heap-insert", "Add a value and sift it up.", new("O(1)", "O(1)", "O(log n)", "O(1)", "On average a new value only rises a level or two."), [Value with { Default = 5 }]),
                new("extract", "Extract Min", "heap-extract", "Remove the root and restore the heap.", new("O(log n)", "O(log n)", "O(log n)", "O(1)", "Sift-down walks one root-to-leaf path."), []),
                new("peek", "Peek", "heap-peek", "Read the minimum.", new("O(1)", "O(1)", "O(1)", "O(1)", "It's always items[0]."), []),
            ],
            new(0, 15, 0, 99, [12, 25, 18, 40, 31, 22, 30, 47]),
            "heap", v => new HeapSession(v, 15), Phase: 2),

        new("hash-table", "Hash Table", Advanced, Difficulty.Intermediate,
            "Hashing into buckets with separate chaining.",
            "A hash table turns a key into a bucket index with a hash function, here key % 7. Lookups jump straight to one bucket instead of searching everything. Different keys can land in the same bucket (a collision), so each bucket holds a small linked chain. As long as chains stay short, insert, search and delete take O(1) on average. HashSet<T> and Dictionary<TKey, TValue> work this way.",
            ["hash(key) = key % buckets picks the bucket in O(1).", "Collisions are chained in a linked list.", "Load factor = count / buckets; real tables resize when it grows.", "Worst case O(n): every key in one bucket."],
            [
                new("insert", "Insert", "hash-insert", "Hash the key and prepend it to its chain.", new("O(1)", "O(1)", "O(n)", "O(1)", "Checks the chain for duplicates first."), [Value with { Default = 24, Min = 0 }]),
                new("search", "Search", "hash-search", "Hash the key and scan only its chain.", new("O(1)", "O(1)", "O(n)", "O(1)", "Average cost depends on the load factor."), [Value with { Default = 17, Min = 0 }]),
                new("delete", "Delete", "hash-delete", "Unlink the key from its chain.", new("O(1)", "O(1)", "O(n)", "O(1)", ""), [Value with { Default = 10, Min = 0 }]),
            ],
            new(0, 20, 0, 99, [10, 3, 17, 22, 31, 8, 45, 15]),
            "hash", v => new HashSession(v, 7), Phase: 2),

        new("graph-traversal", "Graph & BFS / DFS", Graphs, Difficulty.Intermediate,
            "Adjacency lists, breadth-first and depth-first search.",
            "A graph is a set of vertices joined by edges. Here it is stored as adjacency lists: each vertex keeps a list of (neighbour, weight) pairs. BFS explores level by level using a queue, so it finds paths with the fewest edges. DFS dives as deep as possible using recursion (the call stack) and backtracks.",
            ["Adjacency lists use O(V + E) memory.", "BFS uses a queue; DFS uses recursion, i.e. a stack.", "Both mark vertices visited so cycles don't loop forever.", "Green edges show the traversal tree."],
            [
                new("bfs", "BFS", "bfs", "Breadth-first search from a start vertex.", new("O(V + E)", "O(V + E)", "O(V + E)", "O(V)", "Each vertex is enqueued once; each edge is checked twice."), [Start]),
                new("dfs", "DFS", "dfs", "Depth-first search from a start vertex.", new("O(V + E)", "O(V + E)", "O(V + E)", "O(V)", "Space is the recursion depth."), [Start]),
                new("neighbors", "Adjacency", "graph-neighbors", "List a vertex's neighbours from its adjacency list.", new("O(deg)", "O(deg)", "O(deg)", "O(deg)", "Only the vertex's own list is scanned."), [Start]),
            ],
            GraphInput, "graph", null, Phase: 2) { CreateFromText = t => new GraphSession(Graph.Parse(t)) },

        new("dijkstra", "Dijkstra's Algorithm", Graphs, Difficulty.Advanced,
            "Shortest paths from one source with a priority queue.",
            "Dijkstra's algorithm finds the cheapest path from a source to every vertex when all edge weights are non-negative. It repeatedly settles the closest unsettled vertex (taken from a min-priority queue) and relaxes its edges: if going through it is cheaper, the neighbour's distance is lowered. Numbers under each vertex are the current best distances.",
            ["Requires non-negative weights.", "Once a vertex is dequeued, its distance is final.", "Stale queue entries are skipped instead of decreased.", "Green edges form the shortest-path tree."],
            [new("dijkstra", "Dijkstra", "dijkstra", "Compute shortest distances from the source.", new("O((V+E) log V)", "O((V+E) log V)", "O((V+E) log V)", "O(V + E)", "Using .NET's binary-heap PriorityQueue."), [Start with { Label = "Source" }])],
            GraphInput, "graph", null, Phase: 2) { CreateFromText = t => new GraphSession(Graph.Parse(t)) },

        new("mst", "Minimum Spanning Tree", Graphs, Difficulty.Advanced,
            "Kruskal and Prim connect every vertex as cheaply as possible.",
            "A minimum spanning tree connects all vertices using V − 1 edges with the smallest total weight. Kruskal sorts all edges and adds each one that doesn't create a cycle, using union–find to check. Prim grows a single tree from a start vertex, always adding the cheapest edge that leaves it. Both are greedy and both are optimal.",
            ["A spanning tree has exactly V − 1 edges and no cycles.", "Kruskal: global cheapest edge, skip cycles (union–find).", "Prim: cheapest edge leaving the current tree.", "Faded edges were rejected."],
            [
                new("kruskal", "Kruskal", "kruskal", "Add edges lightest-first, skipping cycles.", new("O(E log E)", "O(E log E)", "O(E log E)", "O(V)", "Dominated by sorting the edges."), []),
                new("prim", "Prim", "prim", "Grow the tree from a start vertex.", new("O(E log V)", "O(E log V)", "O(E log V)", "O(E)", "Lazy version: the queue may hold stale edges."), [Start]),
            ],
            GraphInput, "graph", null, Phase: 2) { CreateFromText = t => new GraphSession(Graph.Parse(t)) },

        new("recursion-tree", "Recursion Tree", Advanced, Difficulty.Intermediate,
            "Watch every call a recursive function makes.",
            "A recursive function solves a problem by calling itself on smaller inputs until it reaches a base case. Drawing every call as a node shows the true cost: naive Fibonacci recomputes the same subproblems over and over (exponential), memoization caches them (linear), and factorial makes a single chain of calls.",
            ["Every call needs a base case to stop.", "Blue = executing, outlined = waiting on the call stack, green = returned.", "Naive fib(n) makes O(2ⁿ) calls.", "Memoized results (teal) cut the tree down to O(n)."],
            [
                new("fib", "Fibonacci", "fib", "Naive recursive Fibonacci.", new("O(2ⁿ)", "O(2ⁿ)", "O(2ⁿ)", "O(n)", "Space is the maximum call-stack depth."), [new("n", "n (0–6)", 5, 0, 6)]),
                new("fib-memo", "Memoized", "fib-memo", "Fibonacci with a memo dictionary.", new("O(n)", "O(n)", "O(n)", "O(n)", "Each fib(k) is computed once; repeats hit the cache."), [new("n", "n (0–10)", 6, 0, 10)]),
                new("factorial", "Factorial", "factorial", "n! = n × (n − 1)!", new("O(n)", "O(n)", "O(n)", "O(n)", "A single chain of n calls."), [new("n", "n (0–8)", 5, 0, 8)]),
            ],
            new(0, 0, 0, 0, []) { Kind = InputKind.None },
            "tree", _ => new RecursionSession(), Phase: 2),
        Soon("segment-tree", "Segment Tree", Trees, Difficulty.Advanced, "Range queries and updates in O(log n).", "tree", 3),
        Soon("union-find", "Union-Find", Advanced, Difficulty.Advanced, "Disjoint sets with path compression.", "graph", 3),
        Soon("dynamic-programming", "Dynamic Programming", Advanced, Difficulty.Advanced, "Fill a table of overlapping subproblems.", "hash", 3),
        Soon("trie", "Trie", Trees, Difficulty.Intermediate, "Prefix tree for fast string lookup.", "tree", 3),
    ];

    public static AlgorithmDef? Find(string slug) =>
        All.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private static AlgorithmDef Soon(string slug, string name, Category category, Difficulty d, string tagline, string preview, int phase) =>
        new(slug, name, category, d, tagline, "", [], [], new(0, 0, 0, 0, []), preview, null, phase);
}
