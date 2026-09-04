# PoW tests and speed comparison

Run the correctness tests:

```sh
dotnet test PowBench.Tests
```

Compare solving speed:

```sh
dotnet run -c Release --project PowBench
```

Optional arguments are challenge count, difficulty bits, and rounds (defaults: `10 31 3`):

```sh
dotnet run -c Release --project PowBench -- 20 31 5
```

The comparison uses the same seeded challenges and starts each solve at counter zero. It warms up each variant, rotates their order between rounds, and verifies every result against the current implementation. The table reports whole-solve mean, median and P95 times, plus speedup relative to `Current` (higher is faster). These are local Stopwatch measurements; use multiple rounds in Release with little other CPU activity.

`SolverVariants.cs` contains the current implementation and three alternatives: rewriting the counter instead of incrementing ASCII, static SHA-256 hashing instead of a reused instance, and checking a hex string instead of extracting nibbles. The application continues to use the current implementation.

To compare another algorithm, derive from `PowSolver`, override `AdvanceCounter`, `TryComputeHash`, or `CheckLeadingBits`, and add a named factory to `SolverVariants.All`. Keep the shared solve loop. The tests automatically run every registered variant against known results, counter boundaries, difficulty prefixes, and UTF-8 keys. Change one step per variant to isolate its effect on solving speed.

The protocol's difficulty counts bits of the **ASCII lowercase hex digest**, not raw hash bits. Increasing this setting can make runs much longer.
