using System.Security.Cryptography;
using AlbionDataAvalonia.Network.Pow;

internal static class SolverVariants
{
    // Keep the previous implementation first as the timing baseline.
    internal static readonly (string Name, Func<PowSolver> Create)[] All =
    [
        ("Previous", () => new PreviousSolver()),
        ("Scalar", () => new ScalarSolver()),
        ("Incremental only", () => new IncrementalOnlySolver()),
        ("Current", () => new PowSolver()),
        ("Rewrite counter", () => new RewriteCounterSolver()),
        ("Static SHA256", () => new StaticHashSolver()),
        ("Hex string check", () => new HexStringCheckSolver())
    ];

    private class ScalarSolver : PowSolver
    {
        internal override bool UseBatchHashing => false;
    }

    private sealed class PreviousSolver : ScalarSolver
    {
        private readonly SHA256 hash = SHA256.Create();

        internal override void TryComputeHash(ReadOnlySpan<byte> input, Span<byte> hashBuffer) =>
            hash.TryComputeHash(input, hashBuffer, out _);

        internal override bool CheckLeadingBits(ReadOnlySpan<byte> hash, PowDifficulty difficulty) =>
            CheckWithoutPrecheck(hash, difficulty);

        public override void Dispose()
        {
            hash.Dispose();
            base.Dispose();
        }
    }

    private sealed class IncrementalOnlySolver : ScalarSolver
    {
        internal override bool CheckLeadingBits(ReadOnlySpan<byte> hash, PowDifficulty difficulty) =>
            CheckWithoutPrecheck(hash, difficulty);
    }

    private static bool CheckWithoutPrecheck(ReadOnlySpan<byte> hash, PowSolver.PowDifficulty difficulty)
    {
        ReadOnlySpan<byte> hexDigits = "0123456789abcdef"u8;
        ReadOnlySpan<byte> expected = difficulty.ExpectedSpan;
        ReadOnlySpan<byte> masks = difficulty.MaskSpan;
        for (int i = 0; i < expected.Length; i++)
        {
            byte source = hash[i >> 1];
            byte nibble = (i & 1) == 0 ? (byte)(source >> 4) : (byte)(source & 0x0f);
            if (((hexDigits[nibble] ^ expected[i]) & masks[i]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class RewriteCounterSolver : ScalarSolver
    {
        internal override void AdvanceCounter(Span<byte> counterSpan, ulong counter) =>
            WriteCounterHex(counterSpan, counter);
    }

    private sealed class StaticHashSolver : ScalarSolver
    {
        internal override void TryComputeHash(ReadOnlySpan<byte> input, Span<byte> hashBuffer) =>
            SHA256.HashData(input, hashBuffer);
    }

    private sealed class HexStringCheckSolver : ScalarSolver
    {
        internal override bool CheckLeadingBits(ReadOnlySpan<byte> hash, PowDifficulty difficulty)
        {
            string hex = Convert.ToHexStringLower(hash);
            for (int i = 0; i < difficulty.ExpectedSpan.Length; i++)
            {
                if (((hex[i] ^ difficulty.ExpectedSpan[i]) & difficulty.MaskSpan[i]) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
