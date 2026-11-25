# AlgoSharp

AlgoSharp is an interactive data structures and algorithms visualizer built with C# / .NET 10 and Blazor Server. You can play through real C# implementations step by step, forwards and backwards. The line that's running is highlighted, and the variables in scope are shown as they change.

```powershell
dotnet run --project src/AlgoSharp.Web --launch-profile http   # http://localhost:5295
dotnet test
```

## What's included

| Phase | Area | Visualizers |
| --- | --- | --- |
| 1 | Arrays | Access, Insert, Delete, Update, Linear Search |
| 1 | Sorting | Bubble, Selection, Insertion, Merge, Quick (Lomuto) |
| 1 | Searching | Linear Search, Binary Search |
| 1 | Linked List | Insert, Delete, Search, Traverse |
| 1 | Stack & Queue | Push / Pop / Peek, Enqueue / Dequeue / Peek (circular array) |
| 2 | Binary Search Tree | Insert, Search, Delete (0/1/2 children), In-order traversal |
| 2 | Binary Heap | Insert (sift up), Extract Min (sift down), Peek, with tree and array views |
| 2 | Hash Table | Insert, Search, Delete with separate chaining |
| 2 | Graphs | Adjacency lists, BFS, DFS, Dijkstra, Kruskal and Prim MST (editable edge list) |
| 2 | Recursion Tree | Naive Fibonacci, memoized Fibonacci, Factorial |
| 3 | Segment Tree | Range sum query, point update, build (array-backed, recursive) |
| 3 | Union-Find | Union by rank, Find with path compression, Connected |
| 3 | Dynamic Programming | LCS with traceback, coin change, bottom-up Fibonacci (live DP tables) |
| 3 | Trie | Insert, search, starts-with |
| 3 | Network Flow | Edmonds–Karp max flow with flow/capacity labels and residual paths |
| 3 | Topological Sort | Kahn's algorithm with live in-degrees |

Each visualizer has play, pause, step forward and back, a timeline you can scrub, seven speeds, custom and random input, variables, complexity, and a beginner-friendly explanation for every step. Keyboard: `Space` play, `←`/`→` step, `Home`/`End`, `R` run again, `/` search.

## How it works

```
src/AlgoSharp.Core      algorithms + step engine (no UI dependencies)
  Algorithms/           the real C# that runs and is displayed
  Engine/               ArrayTracer, ListTracer, Step snapshots, CodeLibrary
  Catalog/              algorithm metadata + per-page sessions
src/AlgoSharp.Web       Blazor UI
  Components/Viz/       ArrayCanvas, ListCanvas, CodeViewer, PlaybackControls, ...
  Services/Player.cs    deterministic playback (single cancellable loop)
tests/AlgoSharp.Core.Tests
```

- **C# owns the logic.** Algorithms in `Algorithms/` call a tracer (`T.At("cmp").Hl(...).Say(...)`) that records full snapshots. Every step is a complete state, so stepping backward is just showing an earlier snapshot. Nothing is replayed.
- **The displayed code is the code that runs.** Algorithm files are embedded in the assembly. `CodeLibrary` cuts out each `// <code id="...">` region, hides lines that start with `T.`, and maps `// #label` markers to line numbers. Tests check that every recorded step points at a real line.
- **Trees and graphs.** `GraphTracer` takes snapshots of the real structure as positioned nodes and edges. `TreeLayout` handles BSTs, heaps, call trees, tries and union-find forests. `GraphCanvas` draws them in a coordinate space that scales with the container.
- **DP tables.** `TableTracer` reads the algorithm's own DP array, and shows only the cells that have been computed so far.
- **Identity-tracked rendering.** Cells and nodes keep stable ids, so Blazor's keyed diffing combined with CSS transitions animates swaps, shifts, merges and pointer changes. No JavaScript animation library is needed. Linked-list snapshots come from walking the real `Node` objects.

## Adding an algorithm

1. Write it in `src/AlgoSharp.Core/Algorithms/`, wrapped in a `// <code id="my-algo">` … `// </code>` region, with `// #label` markers and `T.` step calls.
2. Register it in `AlgorithmCatalog.All` with its complexity, description and a session.
3. `dotnet test`: `CodeSyncTests` will flag any step label that doesn't map to a line.
