using System.Diagnostics;
using System.Text;
using AlbionDataAvalonia.Network.Pow;

internal static class Program
{
    private const int RandomnessBytes = 3;
    private const int DifficultyBits = 39;
    private const int ChallengeCount = 100;
    private const int CounterHexLength = 16;
    private static readonly double TickToNanoseconds = 1_000_000_000d / Stopwatch.Frequency;

    private static void Main()
    {
        Console.WriteLine("PowSolver timing (Stopwatch)");
        Console.WriteLine($"Randomness bytes: {RandomnessBytes}, difficulty bits: {DifficultyBits}");
        Console.WriteLine($"Challenge count : {ChallengeCount}");
        Console.WriteLine();

        PowRequest[] challenges = PowChallengeFactory.CreateStableChallenges(ChallengeCount, RandomnessBytes, DifficultyBits);
        Console.WriteLine($"Generated {challenges.Length} stable challenges.");

        TimeSolvePow(challenges);

        if (challenges.Length == 0)
        {
            return;
        }

        MeasureSteps(challenges);
    }

    private static void TimeSolvePow(PowRequest[] challenges)
    {
        if (challenges.Length == 0)
        {
            Console.WriteLine("No challenges to solve.");
            return;
        }

        var samples = new double[challenges.Length];
        double totalMs = 0;
        double minMs = double.MaxValue;
        double maxMs = double.MinValue;

        for (int i = 0; i < challenges.Length; i++)
        {
            var solver = new PowSolver();
            solver.ResetCounter(0);
            var sw = Stopwatch.StartNew();
            solver.ProcessPow(challenges[i]);
            sw.Stop();

            double elapsed = sw.Elapsed.TotalMilliseconds;
            samples[i] = elapsed;
            totalMs += elapsed;
            minMs = Math.Min(minMs, elapsed);
            maxMs = Math.Max(maxMs, elapsed);
        }

        Array.Sort(samples);
        double meanMs = totalMs / samples.Length;
        double medianMs = Percentile(samples, 50);
        double p95Ms = Percentile(samples, 95);

        Console.WriteLine();
        Console.WriteLine("SolvePow statistics:");
        Console.WriteLine($"  Total ms : {totalMs:F3}");
        Console.WriteLine($"  Mean  ms : {meanMs:F3}");
        Console.WriteLine($"  Median   : {medianMs:F3}");
        Console.WriteLine($"  95th pct : {p95Ms:F3}");
        Console.WriteLine($"  Min   ms : {minMs:F3}");
        Console.WriteLine($"  Max   ms : {maxMs:F3}");
        Console.WriteLine();
    }

    private static void MeasureSteps(PowRequest[] challenges)
    {
        if (challenges.Length == 0)
        {
            Console.WriteLine("No steps to measure.");
            return;
        }

        Console.WriteLine("Step timings:");

        StepStats writeStats = MeasureStep(challenges, StepTarget.WriteCounterHex);
        StepStats hashStats = MeasureStep(challenges, StepTarget.TryComputeHash);
        StepStats checkStats = MeasureStep(challenges, StepTarget.CheckLeadingBits);

        PrintStepStats("WriteCounterHex", writeStats);
        PrintStepStats("TryComputeHash", hashStats);
        PrintStepStats("CheckLeadingBits", checkStats);
    }

    private static StepStats MeasureStep(PowRequest[] challenges, StepTarget target)
    {
        var perStepIterationNanoseconds = new double[challenges.Length];
        var perIterationTotalNanoseconds = new double[challenges.Length];
        var perSolveStepMilliseconds = new double[challenges.Length];
        double totalStepNanoseconds = 0;
        double totalIterationNanoseconds = 0;
        double totalSolveStepMilliseconds = 0;
        long totalIterations = 0;
        double totalIterationsPerSolve = 0;

        for (int i = 0; i < challenges.Length; i++)
        {
            SingleStepResult result = MeasureStepForChallenge(challenges[i], target);
            double stepPerIterationNs = result.Iterations > 0 ? result.StepNanoseconds / result.Iterations : 0;
            double iterationPerIterationNs = result.Iterations > 0 ? result.TotalIterationNanoseconds / result.Iterations : 0;
            double stepPerSolveMs = result.StepNanoseconds / 1_000_000.0;

            perStepIterationNanoseconds[i] = stepPerIterationNs;
            perIterationTotalNanoseconds[i] = iterationPerIterationNs;
            perSolveStepMilliseconds[i] = stepPerSolveMs;

            totalStepNanoseconds += result.StepNanoseconds;
            totalIterationNanoseconds += result.TotalIterationNanoseconds;
            totalSolveStepMilliseconds += stepPerSolveMs;
            totalIterations += result.Iterations;
            totalIterationsPerSolve += result.Iterations;
        }

        Array.Sort(perStepIterationNanoseconds);
        Array.Sort(perIterationTotalNanoseconds);
        Array.Sort(perSolveStepMilliseconds);

        double stepMeanNs = totalIterations > 0 ? totalStepNanoseconds / totalIterations : 0;
        double stepMedianNs = Percentile(perStepIterationNanoseconds, 50);
        double stepP95Ns = Percentile(perStepIterationNanoseconds, 95);

        double iterationMeanNs = totalIterations > 0 ? totalIterationNanoseconds / totalIterations : 0;
        double iterationMedianNs = Percentile(perIterationTotalNanoseconds, 50);
        double iterationP95Ns = Percentile(perIterationTotalNanoseconds, 95);

        double stepMeanSolveMs = perSolveStepMilliseconds.Length > 0 ? totalSolveStepMilliseconds / perSolveStepMilliseconds.Length : 0;
        double stepMedianSolveMs = Percentile(perSolveStepMilliseconds, 50);
        double stepP95SolveMs = Percentile(perSolveStepMilliseconds, 95);
        double meanIterationsPerSolve = perSolveStepMilliseconds.Length > 0 ? totalIterationsPerSolve / perSolveStepMilliseconds.Length : 0;

        return new StepStats(
            stepMeanNs,
            stepMedianNs,
            stepP95Ns,
            iterationMeanNs,
            iterationMedianNs,
            iterationP95Ns,
            stepMeanSolveMs,
            stepMedianSolveMs,
            stepP95SolveMs,
            totalIterations,
            meanIterationsPerSolve);
    }

    private static SingleStepResult MeasureStepForChallenge(PowRequest challenge, StepTarget target)
    {
        var solver = new PowSolver();
        solver.ResetCounter(0);

        ReadOnlySpan<byte> prefix = "aod^"u8;
        int prefixLength = prefix.Length;
        byte[] suffix = Encoding.UTF8.GetBytes($"^{challenge.Key}");
        int totalLength = prefixLength + CounterHexLength + suffix.Length;

        byte[] inputBuffer = new byte[totalLength];
        prefix.CopyTo(inputBuffer);
        suffix.CopyTo(inputBuffer.AsSpan(prefixLength + CounterHexLength));

        PowSolver.PowDifficulty difficulty = PowSolver.PowDifficulty.Create(challenge.Wanted);
        Span<byte> counterSpan = inputBuffer.AsSpan(prefixLength, CounterHexLength);
        Span<byte> hashBuffer = stackalloc byte[32];

        ulong counter = 0;
        double totalStepNanoseconds = 0;
        double totalIterationNanoseconds = 0;
        long iterations = 0;

        while (true)
        {
            long iterationStart = Stopwatch.GetTimestamp();

            if (target == StepTarget.WriteCounterHex)
            {
                long start = Stopwatch.GetTimestamp();
                PowSolver.WriteCounterHex(counterSpan, counter);
                long end = Stopwatch.GetTimestamp();
                totalStepNanoseconds += (end - start) * TickToNanoseconds;
                counter++;
            }
            else
            {
                PowSolver.WriteCounterHex(counterSpan, counter++);
            }

            if (target == StepTarget.TryComputeHash)
            {
                long start = Stopwatch.GetTimestamp();
                solver.TryComputeHash(inputBuffer, hashBuffer);
                long end = Stopwatch.GetTimestamp();
                totalStepNanoseconds += (end - start) * TickToNanoseconds;
            }
            else
            {
                solver.TryComputeHash(inputBuffer, hashBuffer);
            }

            bool isMatch;
            if (target == StepTarget.CheckLeadingBits)
            {
                long start = Stopwatch.GetTimestamp();
                isMatch = PowSolver.CheckLeadingBits(hashBuffer, difficulty);
                long end = Stopwatch.GetTimestamp();
                totalStepNanoseconds += (end - start) * TickToNanoseconds;
            }
            else
            {
                isMatch = PowSolver.CheckLeadingBits(hashBuffer, difficulty);
            }

            iterations++;

            long iterationEnd = Stopwatch.GetTimestamp();
            totalIterationNanoseconds += (iterationEnd - iterationStart) * TickToNanoseconds;

            if (isMatch)
            {
                break;
            }
        }

        return new SingleStepResult(totalStepNanoseconds, totalIterationNanoseconds, iterations);
    }

    private static void PrintStepStats(string name, StepStats stats)
    {
        Console.WriteLine($"  {name,-16} step/iter mean {stats.StepMeanNanoseconds:F1} ns, median {stats.StepMedianNanoseconds:F1} ns, 95th {stats.StepNinetyFifthNanoseconds:F1} ns (iters {stats.TotalIterations})");
        Console.WriteLine($"  {string.Empty,-16} iter total mean {stats.IterationMeanNanoseconds:F1} ns, median {stats.IterationMedianNanoseconds:F1} ns, 95th {stats.IterationNinetyFifthNanoseconds:F1} ns");
        Console.WriteLine($"  {string.Empty,-16} step/solve mean {stats.MeanSolveMilliseconds:F3} ms, median {stats.MedianSolveMilliseconds:F3} ms, 95th {stats.NinetyFifthSolveMilliseconds:F3} ms, iterations/solve {stats.MeanIterationsPerSolve:F1}");
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        double position = (percentile / 100d) * (sortedSamples.Length - 1);
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedSamples[lowerIndex];
        }

        double fraction = position - lowerIndex;
        return sortedSamples[lowerIndex] + fraction * (sortedSamples[upperIndex] - sortedSamples[lowerIndex]);
    }

    private enum StepTarget
    {
        WriteCounterHex,
        TryComputeHash,
        CheckLeadingBits
    }

    private readonly record struct StepStats(
        double StepMeanNanoseconds,
        double StepMedianNanoseconds,
        double StepNinetyFifthNanoseconds,
        double IterationMeanNanoseconds,
        double IterationMedianNanoseconds,
        double IterationNinetyFifthNanoseconds,
        double MeanSolveMilliseconds,
        double MedianSolveMilliseconds,
        double NinetyFifthSolveMilliseconds,
        long TotalIterations,
        double MeanIterationsPerSolve);

    private readonly record struct SingleStepResult(
        double StepNanoseconds,
        double TotalIterationNanoseconds,
        long Iterations);

    private static class PowChallengeFactory
    {
        private const int Seed = 123456789;

        public static PowRequest[] CreateStableChallenges(int count, int randomnessBytes, int difficultyBits)
        {
            if (count <= 0)
            {
                return Array.Empty<PowRequest>();
            }

            var rng = new Random(Seed);
            var result = new PowRequest[count];

            for (int i = 0; i < count; i++)
            {
                string wantedHex = RandomHex(rng, randomnessBytes);
                string keyHex = RandomHex(rng, randomnessBytes);
                string bits = HexToAsciiBits(wantedHex, difficultyBits);

                result[i] = new PowRequest
                {
                    Key = keyHex,
                    Wanted = bits
                };
            }

            return result;
        }

        private static string RandomHex(Random rng, int byteCount)
        {
            byte[] buffer = new byte[byteCount];
            rng.NextBytes(buffer);
            return Convert.ToHexString(buffer).ToLowerInvariant();
        }

        private static string HexToAsciiBits(string hex, int bitCount)
        {
            if (bitCount <= 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(bitCount);

            foreach (char c in hex)
            {
                int ascii = c;
                for (int bit = 7; bit >= 0 && sb.Length < bitCount; bit--)
                {
                    sb.Append(((ascii >> bit) & 1) == 1 ? '1' : '0');
                }

                if (sb.Length == bitCount)
                {
                    break;
                }
            }

            if (sb.Length < bitCount)
            {
                sb.Append('0', bitCount - sb.Length);
            }

            return sb.ToString();
        }
    }
}
