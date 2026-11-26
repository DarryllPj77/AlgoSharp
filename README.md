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

## Problem solving

`/problems` shows one problem solved two ways, on the same input:

- **Two Sum:** brute force (nested loops, O(n²)) vs a hash map (`Dictionary<int, int>`, O(n)). You can watch either approach on its own or both side by side on a shared timeline. Each one has its own C# line highlighting, a live dictionary view, and live counters for comparisons, lookups and insertions.
- **Performance comparison:** puts Big-O (theory) next to the operations actually counted on your input. It also reruns the same code on worst-case inputs with n = 10, 100 and 1,000, with step recording off and only the counters on.

To add a problem, write each approach in `Algorithms/` (wrapped in a `// <code id>` region, using `T.Tick(...)` for counters) and register a `ProblemDef` in `Problems/ProblemCatalog.cs`. The page, comparison mode and scaling chart work without changes.

## Code Lab: write your own C# and watch it run

`/code/two-sum` has a Monaco editor where you write your own solution, with any variable names or style. Press **Visualize** to step through what your code actually did.

1. **Compile (server, no execution).** Roslyn compiles the code and reports errors live. A check rejects APIs a solution doesn't need (file I/O, network, reflection, threads, `Environment`, …) and constructs the visualizer can't trace (`goto`, `async`, `yield`, `unsafe`, …).
2. **Instrument.** `AlgoSharp.Playground/Compilation/Instrumenter.cs` rewrites your syntax tree to report each statement, condition (with its true/false result), variable write, array/`List` read (with the index variable's name), dictionary/set lookup (hit or miss), method entry and return, and the exception site. Your names, logic and line numbers are unchanged.
3. **Execute (sandbox only).** The instrumented assembly runs in a fresh Docker container. The container has no network, a read-only root filesystem, runs as user `nobody`, has no capabilities and no privilege escalation, and is limited to 256 MB of memory, 1 CPU and 64 processes. There's also a 15 s timeout and an event budget that stops infinite loops, even ones hidden inside `try/catch`. **User code never runs in the web server process.** Without Docker, execution is disabled and the page says so.
4. **Replay.** The trace becomes frames for the existing visual components. Arrays are shown as cells, with pointers named after the index variables your code actually used. Dictionaries and sets are shown as key/value chips, with lookups marked as hits or misses. Conditions appear as callouts, and the current line is highlighted in the editor.

Results are checked by the problem's own rule (any valid Two Sum pair is accepted). A learning assistant labels the approach and gives complexity estimates, saying how confident it is. It suggests next steps but never claims a solution is optimal, and offers progressive hints.

Enable execution (Docker Desktop with Linux containers must be running):

```powershell
./sandbox/build.ps1      # publishes AlgoSharp.Runner and builds the algosharp-runner:1 image
```

Settings live under `Sandbox` in `appsettings.json` (`Mode: Docker|Disabled`, image, timeout, limits).

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
