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

The batch path handles a single padded SHA-256 block: up to 55 input bytes, or 34 UTF-8 key bytes for `aod^<16 hex digits>^<key>`. It requires a difficulty that fixes the first raw hash byte (two complete ASCII hex characters). Other inputs and CPUs without AVX2 automatically use the scalar path from the previous optimization pass: a reused incremental SHA-256 hasher and first-byte precheck. The benchmark prints whether AVX2 is available. `PowSha256Batch.cs` implements the SHA-256 schedule and compression from [FIPS 180-4, section 6.2](https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf).

`SolverVariants.cs` keeps `Previous` (the original reused `SHA256` instance and nibble check), `Scalar` (the previous optimization pass), `Incremental only` (without the precheck), and the counter-rewrite, static-SHA256, and hex-string alternatives. To compare another scalar step, derive from `ScalarSolver` in that file, override `AdvanceCounter`, `TryComputeHash`, or `CheckLeadingBits`, and add a named factory to `SolverVariants.All`. This keeps batching disabled so the chosen step is measured on every attempt. Change one step per variant to isolate its effect.

The tests automatically exercise every registered variant. They also compare every SIMD lane with .NET SHA-256 across random keys, counter carries, wraparound, and message-length boundaries; check all possible winning lanes and multiple candidates in one batch; and verify fallback for long and multibyte UTF-8 keys.

The protocol's difficulty counts bits of the **ASCII lowercase hex digest**, not raw hash bits. Increasing this setting can make runs much longer.

Windows/.NET 10 Release measurements on 2026-09-04, using seven rotated rounds on one thread:

| Difficulty bits | Challenges | Previous mean | Scalar mean | Current mean | Speedup over Scalar |
| --- | --- | --- | --- | --- | --- |
| 31 | 40 | 2.857 ms | 2.621 ms | 1.106 ms | 2.37x |
| 41 | 8 | 207.383 ms | 189.283 ms | 79.266 ms | 2.39x |

All solutions matched. To repeat the harder run: `dotnet run -c Release --project PowBench -- 8 41 7`. Rerun on the target machine; timings depend on the workload and platform.
