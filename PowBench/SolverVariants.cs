using System.Security.Cryptography;
using AlbionDataAvalonia.Network.Pow;

internal static class SolverVariants
{
    // Each variant changes one step while using the production solve loop.
    internal static readonly (string Name, Func<PowSolver> Create)[] All =
    [
        ("Current", () => new PowSolver()),
        ("Rewrite counter", () => new RewriteCounterSolver()),
        ("Static SHA256", () => new StaticHashSolver()),
        ("Hex string check", () => new HexStringCheckSolver())
    ];

    private sealed class RewriteCounterSolver : PowSolver
    {
        internal override void AdvanceCounter(Span<byte> counterSpan, ulong counter) =>
            WriteCounterHex(counterSpan, counter);
    }

    private sealed class StaticHashSolver : PowSolver
    {
        internal override void TryComputeHash(ReadOnlySpan<byte> input, Span<byte> hashBuffer) =>
            SHA256.HashData(input, hashBuffer);
    }

    private sealed class HexStringCheckSolver : PowSolver
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
