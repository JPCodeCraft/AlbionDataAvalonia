using System.Diagnostics;
using System.Text;
using AlbionDataAvalonia.Network.Pow;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 3
            || !TryReadArgument(args, 0, 10, out int count) || count < 1
            || !TryReadArgument(args, 1, 31, out int difficultyBits) || difficultyBits is < 1 or > 48
            || !TryReadArgument(args, 2, 3, out int rounds) || rounds < 1)
        {
            Console.Error.WriteLine("Usage: dotnet run -c Release --project PowBench -- [challenge-count=10] [difficulty-bits=31 (1..48)] [rounds=3]");
            return 1;
        }

        Console.WriteLine($"PoW comparison: {count} stable challenges, {difficultyBits} difficulty bits, {rounds} rounds.");
        Console.WriteLine($"Batch hashing: {(PowSha256Batch.IsSupported ? "AVX2, eight counters on one thread" : "scalar fallback (AVX2 unavailable)")}.");
#if DEBUG
        Console.WriteLine("Use a Release build for meaningful timings.");
#endif
        PowRequest[] challenges = CreateChallenges(count, difficultyBits);
        var variants = SolverVariants.All;
        var expected = new string[count];
        using (var baseline = new PowSolver())
        {
            for (int i = 0; i < count; i++)
            {
                baseline.ResetCounter(0);
                expected[i] = baseline.ProcessPow(challenges[i]);
            }
        }

        // Warm every variant before measuring; construction and validation are outside the timer.
        foreach (var variant in variants)
        {
            using var solver = variant.Create();
            long warmupStart = Stopwatch.GetTimestamp();
            do
            {
                solver.ResetCounter(0);
                solver.ProcessPow(challenges[0]);
            }
            while (Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < 250);
        }

        var samples = variants.Select(_ => new List<double>()).ToArray();
        for (int round = 0; round < rounds; round++)
        {
            // Rotate the order to reduce the advantage of running first or last.
            for (int offset = 0; offset < variants.Length; offset++)
            {
                int variantIndex = (round + offset) % variants.Length;
                var variant = variants[variantIndex];
                using var solver = variant.Create();
                for (int i = 0; i < challenges.Length; i++)
                {
                    solver.ResetCounter(0);
                    long start = Stopwatch.GetTimestamp();
                    string solution = solver.ProcessPow(challenges[i]);
                    double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                    if (solution != expected[i])
                    {
                        Console.Error.WriteLine($"{variant.Name} returned {solution}; expected {expected[i]} for challenge {i}.");
                        return 1;
                    }

                    samples[variantIndex].Add(elapsedMs);
                }
            }

            Console.WriteLine($"Completed round {round + 1}/{rounds}.");
        }

        double baselineMean = samples[0].Average();
        Console.WriteLine();
        Console.WriteLine($"{"Algorithm",-22} {"Mean ms",10} {"Median ms",10} {"P95 ms",10} {"Speedup",10}");
        for (int i = 0; i < variants.Length; i++)
        {
            double[] sorted = samples[i].Order().ToArray();
            double mean = sorted.Average();
            double median = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
            double p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
            Console.WriteLine($"{variants[i].Name,-22} {mean,10:F3} {median,10:F3} {p95,10:F3} {baselineMean / mean,9:F2}x");
        }

        Console.WriteLine($"Speedup is relative to {variants[0].Name}; higher is faster. All solutions matched.");
        return 0;
    }

    private static bool TryReadArgument(string[] args, int index, int fallback, out int value)
    {
        value = fallback;
        return index >= args.Length || int.TryParse(args[index], out value);
    }

    private static PowRequest[] CreateChallenges(int count, int difficultyBits)
    {
        var random = new Random(123456789);
        var challenges = new PowRequest[count];
        var bytes = new byte[3];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(bytes);
            string wantedHex = Convert.ToHexStringLower(bytes);
            random.NextBytes(bytes);
            string key = Convert.ToHexStringLower(bytes);
            string bits = string.Concat(Encoding.ASCII.GetBytes(wantedHex)
                .Select(value => Convert.ToString(value, 2).PadLeft(8, '0')));
            challenges[i] = new PowRequest { Key = key, Wanted = bits[..difficultyBits] };
        }

        return challenges;
    }
}
