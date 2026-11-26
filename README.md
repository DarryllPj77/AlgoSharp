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

## Problem solving: Solve it

`/problems` lists 28 LeetCode Easy problems (the NeetCode 150 Easy set):
- **Arrays & Hashing:** Contains Duplicate, Valid Anagram, Two Sum
- **Two Pointers:** Valid Palindrome
- **Sliding Window:** Best Time to Buy and Sell Stock
- **Stack:** Valid Parentheses
- **Binary Search:** Binary Search
- **Linked List:** Reverse Linked List, Merge Two Sorted Lists, Linked List Cycle
- **Trees:** Invert Binary Tree, Maximum Depth, Diameter, Balanced Binary Tree, Same Tree, Subtree of Another Tree
- **Heap / Priority Queue:** Kth Largest Element in a Stream, Last Stone Weight
- **1-D Dynamic Programming:** Climbing Stairs, Min Cost Climbing Stairs
- **Intervals:** Meeting Rooms
- **Math & Geometry:** Happy Number, Plus One
- **Bit Manipulation:** Single Number, Number of 1 Bits, Counting Bits, Reverse Bits, Missing Number

**Solve it** (`/problems/{slug}`) opens the coding environment for the problem. There is no predefined solution to pick. Write your own C#, run it against the tests, and step through what it actually did.

Each problem is pure data in `AlgoSharp.Playground/PlaygroundCatalog.cs`:
- description, constraints and examples
- the C# signature, starter code and hints
- typed parameters
- a trusted reference solution (`References.cs`) that gives the expected output for any input, including your own custom cases

`ListNode` and `TreeNode` are predefined as on LeetCode, but you can also declare your own. Lists and trees are entered as arrays, e.g. `[1, 2, 3]` and `[3, 9, 20, null, null, 15, 7]`. Kth Largest is a "design" problem: the class is constructed once, then `Add` is called for each value.

To add a problem, add a `PlaygroundProblem` entry and a reference answer. The page, sandbox and visualizer need no changes. `tests/.../Solutions.cs` holds a correct solution for every problem, and the tests run each one through the full pipeline.

## My Problems: import your own exercises

**Import** (`/problems/import`) turns pasted material into problems. Nothing is saved until you review it.

- **Standard import:** paste ordinary lesson text, e.g. from ChatGPT or a document. Deterministic rules, with no AI API involved:
  - Headings like `Level 12: …`, `Problem 3 - …` or `### Exercise 1: …` start a new problem.
  - Labels like `Concepts:`, `Difficulty:`, `Input:`, `Output:`, `Explanation:` and `Constraints:` are recognized, in plain text or Markdown.
  - Missing details stay empty.
  - A C# signature can be *suggested* from the examples (e.g. `public int CountLocalMaximums(int[] nums)`). It is clearly flagged so you check it.
- **AlgoSharp format:** versioned JSON (`algosharp/problems`, version 1) for reliable imports.
  - **Copy AI prompt** gives you a prompt for ChatGPT, Claude, etc. It is generated from the same schema definition the validator uses (`Library/ImportSchema.cs`), including a complete example.
  - **Validate format** reports field-level errors.
- **Review:** before saving you can edit every field, and remove or skip entries.
  - Examples become test cases that start **unverified**; tick "verified" after checking each expected output.
  - **Challenge** mode (automatic grading) only becomes available with a valid signature and at least one verified test.

**My Problems** (`/problems/mine`):
- search, and filter by difficulty, category and status (Not started / In progress / Solved)
- grouped by lesson or collection
- Solve it, Edit and Delete for each problem
- export and restore a JSON backup

Problems open in the same **Solve It** page, editor, sandbox and visualizer as the practice set:
- **Challenge:** graded against verified tests; passing them all marks the problem solved.
- **Practice:** with a signature, your method runs on the examples and the source's expected output is shown as unverified, without grading. Without a signature, you write `static void Main()` and see its console output and trace.

Your code is saved as a draft automatically.

**Storage:**
- Everything is stored in this browser's **IndexedDB** (`wwwroot/js/store.js`): problems, drafts and progress. Nothing is uploaded.
- The stores are versioned with migrations, and quota errors are reported.
- Data belongs to the current browser profile and is lost if the site's data is cleared, so export backups.
- Records carry `owner`, `revision` and timestamps, so sign-in and cloud sync can be added later without changing the format.

## How Solve It runs your code

The Solve It page has a Monaco editor. Write any solution, in your own style and with your own names, then press **Run & visualize**.

1. **Compile (server, no execution).** Roslyn compiles the code and reports errors live. A check rejects APIs a solution doesn't need (file I/O, network, reflection, threads, `Environment`, …) and constructs the visualizer can't trace (`goto`, `async`, `yield`, `unsafe`, …).
2. **Instrument.** `AlgoSharp.Playground/Compilation/Instrumenter.cs` rewrites your syntax tree to report each statement, condition (with its true/false result), variable write, array/`List` read (with the index variable's name), dictionary/set lookup (hit or miss), method entry and return, and the exception site. Your names, logic and line numbers are unchanged.
3. **Execute (sandbox only).** The instrumented assembly runs in a fresh Docker container. The container has no network, a read-only root filesystem, runs as user `nobody`, has no capabilities and no privilege escalation, and is limited to 256 MB of memory, 1 CPU and 64 processes. There's also a 15 s timeout and an event budget that stops infinite loops, even ones hidden inside `try/catch`. **User code never runs in the web server process.** Without Docker, execution is disabled and the page says so.
4. **Replay.** The trace becomes frames for the existing visual components. Arrays and indexed strings are shown as cells, with pointers named after the index variables your code actually used. Linked lists and trees are drawn as node diagrams, with your variables as pointers and rewired links highlighted. Stacks, queues and heaps are shown as cells too. Dictionaries and sets are shown as key/value chips, with lookups marked as hits or misses. Conditions appear as callouts, and the current line is highlighted in the editor.

Each test case shows the expected output next to yours. Results are checked against the reference answer, or by a custom rule when several answers are valid (e.g. any valid Two Sum pair). A learning assistant labels the approach and gives complexity estimates, saying how confident it is. It suggests next steps but never claims a solution is optimal, and offers progressive hints.

Enable execution (Docker Desktop with Linux containers must be running):

```powershell
./sandbox/build.ps1      # publishes AlgoSharp.Runner and builds the algosharp-runner:1 image
```

Settings live under `Sandbox` in `appsettings.json` (`Mode: Docker|Disabled`, image, timeout, limits).

## Accounts (optional)

Sign up, sign in with a username or email, edit your profile, change your password and sign out of
every device. Accounts use ASP.NET Core Identity on PostgreSQL (Neon): passwords are stored only as
salted hashes, sign-in cookies are HttpOnly (persistent only if you tick "Keep me signed in"), five
wrong passwords lock an account for 15 minutes, and account forms are rate-limited.

Your problem library still lives in your browser, not on the server. Each account gets its own
library on that browser; the guest library you had before signing in is never changed, and My
Problems offers to copy it into your account.

Accounts are off until a database is configured; everything else works without them:

```powershell
dotnet user-secrets set "ConnectionStrings:AlgoSharp" "postgresql://USER:PASSWORD@HOST/DB?sslmode=require" --project src/AlgoSharp.Web
```

Hosting, migrations, Docker Compose, health checks and the optional Vercel landing page are covered
in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

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
