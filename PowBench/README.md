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

The comparison uses the same seeded challenges and starts each solve at counter zero. It warms up each variant for at least 250 ms, rotates their order between rounds, and verifies every result against the current implementation. The table reports whole-solve mean, median and P95 times, plus speedup relative to the first variant, `Previous` (higher is faster). These are local Stopwatch measurements; use multiple rounds in Release with little other CPU activity.

The application uses `Current`: eight SHA-256 attempts at a time using AVX2 on one thread. The fixed message and SHA-256 schedule storage are reused across batches. A first-byte check selects candidates, which are then rehashed with .NET SHA-256 and checked against the full difficulty in counter order. The returned solution and next counter are identical to the scalar solver. No extra worker threads, native libraries, or packages are needed.

Precomputation caches the first four compression rounds, the fixed part of the fifth round, and fixed schedule contributions while the counter's first twelve hex digits stay unchanged. The cache is rebuilt when those digits change (every 65,536 consecutive counters). A batch that crosses this boundary uses the full calculation and invalidates the cache. `No precompute` keeps the earlier SIMD calculation available for comparison.

The batch path handles a single padded SHA-256 block: up to 55 input bytes, or 34 UTF-8 key bytes for `aod^<16 hex digits>^<key>`. It requires a difficulty that fixes the first raw hash byte (two complete ASCII hex characters). Other inputs and CPUs without AVX2 automatically use the scalar path from the previous optimization pass: a reused incremental SHA-256 hasher and first-byte precheck. The benchmark prints whether AVX2 is available. `PowSha256Batch.cs` implements the SHA-256 schedule and compression from [FIPS 180-4, section 6.2](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf).

`SolverVariants.cs` keeps `Previous` (the original reused `SHA256` instance and nibble check), `Scalar` (the previous optimization pass), `Incremental only` (without the precheck), and the counter-rewrite, static-SHA256, and hex-string alternatives. To compare another scalar step, derive from `ScalarSolver` in that file, override `AdvanceCounter`, `TryComputeHash`, or `CheckLeadingBits`, and add a named factory to `SolverVariants.All`. This keeps batching disabled so the chosen step is measured on every attempt. Change one step per variant to isolate its effect.

The tests automatically exercise every registered variant. They also compare every SIMD lane with .NET SHA-256, with precomputation both enabled and disabled, across random keys, counter carries, wraparound, and message-length boundaries. They cover cache reuse and invalidation, all possible winning lanes and multiple candidates in one batch, and fallback for long and multibyte UTF-8 keys.

The protocol's difficulty counts bits of the **ASCII lowercase hex digest**, not raw hash bits. Increasing this setting can make runs much longer.

Windows/.NET 10 Release measurements on 2026-09-04, using AVX2 and eight rotated rounds on one thread. The comparison without precomputation follows the SIMD implementation committed as `0f211db`:

| Difficulty bits | Challenges | No precompute mean | Current mean | Solve time reduction |
| --- | --- | --- | --- | --- |
| 31 | 40 | 1.113 ms | 1.002 ms | 10.0% |
| 41 | 8 | 79.170 ms | 71.191 ms | 10.1% |

On the harder run, `Scalar` averaged 187.888 ms and `Previous` averaged 196.697 ms, making `Current` 2.64x and 2.76x faster, respectively. All solutions matched. To repeat: `dotnet run -c Release --project PowBench -- 8 41 8`. Rerun on the target machine; timings depend on the workload and platform.
