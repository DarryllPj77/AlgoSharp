# AlgoSharp

AlgoSharp is an interactive data structures and algorithms visualizer built with C# / .NET 10 and Blazor Server. You can play through real C# implementations step by step, forwards and backwards. The line that's running is highlighted, and the variables in scope are shown as they change.

```powershell
dotnet run --project src/AlgoSharp.Web --launch-profile http   # http://localhost:5295
dotnet test
```

## What's included (Phase 1)

| Area | Visualizers |
| --- | --- |
| Arrays | Access, Insert, Delete, Update, Linear Search |
| Sorting | Bubble, Selection, Insertion, Merge, Quick (Lomuto) |
| Searching | Linear Search, Binary Search |
| Linked List | Insert, Delete, Search, Traverse |
| Stack & Queue | Push / Pop / Peek, Enqueue / Dequeue / Peek (circular array) |

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
- **Identity-tracked rendering.** Cells and nodes keep stable ids, so Blazor's keyed diffing combined with CSS transitions animates swaps, shifts, merges and pointer changes. No JavaScript animation library is needed. Linked-list snapshots come from walking the real `Node` objects.

## Adding an algorithm

1. Write it in `src/AlgoSharp.Core/Algorithms/`, wrapped in a `// <code id="my-algo">` … `// </code>` region, with `// #label` markers and `T.` step calls.
2. Register it in `AlgorithmCatalog.All` with its complexity, description and a session.
3. `dotnet test`: `CodeSyncTests` will flag any step label that doesn't map to a line.

Phase 2 (BST, heap, hash table, graphs, Dijkstra, MST, recursion tree) and Phase 3 topics are listed as roadmap cards on the homepage.
