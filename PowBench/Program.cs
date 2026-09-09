using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using AlbionDataAvalonia.Network.Pow;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var positional = new List<string>();
        bool cold = false;
        string? selectedVariant = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--cold")
            {
                cold = true;
            }
            else if (args[i] == "--variant" && i + 1 < args.Length)
            {
                selectedVariant = args[++i];
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                return PrintUsage();
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (positional.Count > 4
            || !TryReadArgument(positional, 0, 10, out int count) || count < 1
            || !TryReadArgument(positional, 1, 31, out int difficultyBits) || difficultyBits is < 1 or > 48
            || !TryReadArgument(positional, 2, 3, out int rounds) || rounds < 1
            || !TryReadArgument(positional, 3, 6, out int keyLength) || keyLength is < 0 or > 1024)
        {
            return PrintUsage();
        }

        // Compare cold variants in separate processes so the first solver cannot warm the next one.
        selectedVariant ??= cold ? "Current" : null;
        var variants = SolverVariants.All
            .Where(variant => selectedVariant is null || variant.Name.Equals(selectedVariant, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (variants.Length == 0)
        {
            Console.Error.WriteLine($"Unknown variant: {selectedVariant}.");
            return PrintUsage();
        }

        Console.WriteLine($"PoW comparison: {count} stable challenges, {difficultyBits} difficulty bits, {rounds} rounds, {keyLength}-byte ASCII keys.");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; AVX2: {Avx2.IsSupported}.");
        bool optimizerDisabled = typeof(PowSolver).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled ?? false;
        Console.WriteLine($"Solver JIT optimization: {(optimizerDisabled ? "disabled" : "enabled")}.");
        Console.WriteLine(cold
            ? "Cold mode: no warmup or baseline solve; new solver and async SolvePow per challenge. Only the first solve in this process is cold."
            : "Warm mode: sequential synchronous solves after warmup; solver reused within each round.");
#if DEBUG
        Console.WriteLine("Use a Release build for meaningful timings.");
#endif
        PowRequest[] challenges = CreateChallenges(count, difficultyBits, keyLength);
        var expected = new string[count];
        if (!cold)
        {
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
        }

        var samples = variants.Select(_ => new List<(double ElapsedMs, double Attempts)>()).ToArray();
        for (int round = 0; round < rounds; round++)
        {
            // Rotate the order to reduce the advantage of running first or last.
            for (int offset = 0; offset < variants.Length; offset++)
            {
                int variantIndex = (round + offset) % variants.Length;
                var variant = variants[variantIndex];
                using var reusedSolver = cold ? null : variant.Create();
                for (int i = 0; i < challenges.Length; i++)
                {
                    using var freshSolver = cold ? variant.Create() : null;
                    var solver = freshSolver ?? reusedSolver!;
                    solver.ResetCounter(0);
                    long start = Stopwatch.GetTimestamp();
                    string solution = cold
                        ? await solver.SolvePow(challenges[i])
                        : solver.ProcessPow(challenges[i]);
                    double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                    // Validate only after the timer. Cold mode must never solve a baseline first.
                    if (!IsValidSolution(solution, challenges[i], out ulong counter)
                        || (!cold && solution != expected[i]))
                    {
                        Console.Error.WriteLine($"{variant.Name} returned an invalid or unexpected solution {solution} for challenge {i}.");
                        return 1;
                    }

                    double attempts = (double)counter + 1;
                    samples[variantIndex].Add((elapsedMs, attempts));
                    if (cold && round == 0 && i == 0)
                    {
                        Console.WriteLine($"First solve: {elapsedMs:F3} ms, {attempts:F0} attempts, {attempts / elapsedMs / 1000:F3} MH/s.");
                    }
                }
            }

            Console.WriteLine($"Completed round {round + 1}/{rounds}.");
        }

        double baselineMean = samples[0].Average(sample => sample.ElapsedMs);
        Console.WriteLine();
        Console.WriteLine($"{"Algorithm",-22} {"Mean ms",10} {"Median ms",10} {"P95 ms",10} {"Mean attempts",14} {"MH/s",10} {"Speedup",10}");
        for (int i = 0; i < variants.Length; i++)
        {
            double[] sorted = samples[i].Select(sample => sample.ElapsedMs).Order().ToArray();
            double mean = sorted.Average();
            double median = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
            double p95 = sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1];
            double meanAttempts = samples[i].Average(sample => sample.Attempts);
            double megaHashesPerSecond = samples[i].Sum(sample => sample.Attempts) / sorted.Sum() / 1000;
            Console.WriteLine($"{variants[i].Name,-22} {mean,10:F3} {median,10:F3} {p95,10:F3} {meanAttempts,14:F0} {megaHashesPerSecond,10:F3} {baselineMean / mean,9:F2}x");
        }

        Console.WriteLine($"Speedup is relative to {variants[0].Name}; higher is faster. All solutions validated{(cold ? "." : " and matched the baseline.")}");
        Console.WriteLine("Attempts count searched counters through the solution (counter + 1); MH/s normalizes differing challenge work and excludes any extra SIMD lanes.");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage: dotnet run -c Release --project PowBench -- [challenge-count=10] [difficulty-bits=31 (1..48)] [rounds=3] [key-bytes=6 (0..1024)] [--variant \"Name\"] [--cold]");
        Console.Error.WriteLine($"Variants: {string.Join(", ", SolverVariants.All.Select(variant => variant.Name))}. Cold mode defaults to Current.");
        return 1;
    }

    private static bool TryReadArgument(IReadOnlyList<string> args, int index, int fallback, out int value)
    {
        value = fallback;
        return index >= args.Count || int.TryParse(args[index], out value);
    }

    private static bool IsValidSolution(string solution, PowRequest challenge, out ulong counter)
    {
        if (!ulong.TryParse(solution, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out counter)
            || solution != counter.ToString("x16", CultureInfo.InvariantCulture))
        {
            return false;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"aod^{solution}^{challenge.Key}"));
        string bits = ToAsciiBits(Convert.ToHexStringLower(hash));
        return bits.StartsWith(challenge.Wanted, StringComparison.Ordinal);
    }

    private static PowRequest[] CreateChallenges(int count, int difficultyBits, int keyLength)
    {
        var random = new Random(123456789);
        var challenges = new PowRequest[count];
        var wantedBytes = new byte[3];
        var keyBytes = new byte[(keyLength + 1) / 2];
        for (int i = 0; i < count; i++)
        {
            random.NextBytes(wantedBytes);
            string wantedHex = Convert.ToHexStringLower(wantedBytes);
            random.NextBytes(keyBytes);
            string key = Convert.ToHexStringLower(keyBytes)[..keyLength];
            challenges[i] = new PowRequest { Key = key, Wanted = ToAsciiBits(wantedHex)[..difficultyBits] };
        }

        return challenges;
    }

    private static string ToAsciiBits(string hex) =>
        string.Concat(Encoding.ASCII.GetBytes(hex).Select(value => Convert.ToString(value, 2).PadLeft(8, '0')));
}
