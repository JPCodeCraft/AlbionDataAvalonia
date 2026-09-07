# PoW tests and speed comparison

Run the correctness tests:

```sh
dotnet test PowBench.Tests
```

Compare solving speed:

```sh
dotnet run -c Release --project PowBench
```

Optional arguments are challenge count, difficulty bits, rounds, and ASCII key length in bytes (defaults: `10 31 3 6`):

```sh
dotnet run -c Release --project PowBench -- 20 31 5
```

Select one algorithm with `--variant "Current"`. To measure the first async solve without warming it through a baseline solve, use a fresh process for each variant:

```sh
dotnet run -c Release --project PowBench -- 8 41 3 6 --cold --variant Current
dotnet run -c Release --project PowBench -- 8 41 3 6 --cold --variant Previous
```

Cold mode creates a solver for each challenge and times `await SolvePow`, including thread-pool scheduling, as the app does. Only the first solve in each process is cold; all results are independently verified after timing. The output includes runtime, architecture, AVX2 support, actual solver assembly optimization settings, attempts, and throughput. Change the fourth argument to exercise the long-key scalar fallback, for example `35`. These runs use one upload at a time; the app's upload concurrency and other CPU activity can also affect its reported average.

The comparison uses the same seeded challenges and starts each solve at counter zero. It warms up each variant for at least 250 ms, rotates their order between rounds, and verifies every result against the current implementation. The table reports whole-solve mean, median and P95 times, plus speedup relative to the first variant, `Previous` (higher is faster). These are local Stopwatch measurements; use multiple rounds in Release with little other CPU activity.

The application uses `Current`: eight SHA-256 attempts at a time using AVX2 on one thread. Use Release to evaluate performance: the managed SIMD loop is much slower without JIT optimization. Debug builds use the same backend selection as Release.

The fixed message and SHA-256 schedule storage are reused across batches. The batch hash loop requests full optimization on its first invocation to avoid initially running the expensive intrinsic code at tier 0. All complete ASCII hex characters within the first digest word filter candidates, which are then rehashed with .NET SHA-256 and checked against the full difficulty in counter order. For a 41-bit ASCII difficulty this filters 20 raw digest bits instead of the previous eight, reducing expected candidate rehashes from one in 256 to one in 1,048,576. Partial ASCII characters still require the full check. The returned solution and next counter are identical to the scalar solver. No extra worker threads, native libraries, or packages are needed.

Precomputation caches the first four compression rounds, the fixed part of the fifth round, and fixed schedule contributions while the counter's first twelve hex digits stay unchanged. The cache is rebuilt when those digits change (every 65,536 consecutive counters). A batch that crosses this boundary uses the full calculation and invalidates the cache. `No precompute` keeps the earlier SIMD calculation available for comparison.

The batch path handles a single padded SHA-256 block: up to 55 input bytes, or 34 UTF-8 key bytes for `aod^<16 hex digits>^<key>`. It requires a difficulty that fixes the first raw hash byte (two complete ASCII hex characters). Other inputs and CPUs without AVX2 automatically use the scalar path from the previous optimization pass: a reused incremental SHA-256 hasher and first-byte precheck. The benchmark prints whether AVX2 is available. `PowSha256Batch.cs` implements the SHA-256 schedule and compression from [FIPS 180-4, section 6.2](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf).

`SolverVariants.cs` keeps `Previous` (the original reused `SHA256` instance and nibble check), `Scalar` (the previous optimization pass), `Incremental only` (without the precheck), and the counter-rewrite, static-SHA256, and hex-string alternatives. To compare another scalar step, derive from `ScalarSolver` in that file, override `AdvanceCounter`, `TryComputeHash`, or `CheckLeadingBits`, and add a named factory to `SolverVariants.All`. This keeps batching disabled so the chosen step is measured on every attempt. Change one step per variant to isolate its effect.

The tests automatically exercise every registered variant. They also compare every SIMD lane with .NET SHA-256, with precomputation both enabled and disabled, across random keys, counter carries, wraparound, and message-length boundaries. They cover cache reuse and invalidation, all possible winning lanes and multiple candidates in one batch, fallback for long and multibyte UTF-8 keys, and every partial difficulty through the first-word filter boundary.

The protocol's difficulty counts bits of the **ASCII lowercase hex digest**, not raw hash bits. Increasing this setting can make runs much longer.

Regression investigation on 2026-09-06, Windows x64, .NET 10.0.11, Ryzen 5 3600, one solving thread. The same seeded challenges used six-byte keys and 41 difficulty bits:

| Measurement | Before this fix | After this fix |
| --- | --- | --- |
| Release first async solve, 3,552,099 attempts | 311.005 ms | 170.327 ms |
| Release warmed mean, eight challenges | 72.333 ms | 74.537 ms |

Release means used three rounds; first-solve values are individual fresh-process samples. The change improves first-use performance; warmed Release timings remain similar. `Previous` averaged 207.284 ms in the original Release comparison. All solutions matched.

The reported multi-second slowdown was reproduced in Debug: before these changes, four matched challenges averaged 2,375.864 ms using SIMD versus 253.219 ms using `Scalar` (one round). This is the cost of unoptimized managed hashing; no Debug-specific fallback is added. Settings → Uploads also includes scheduling delays and averages different server challenges. Compare identical build settings, upload concurrency and challenge work before drawing conclusions about installed Release builds.
