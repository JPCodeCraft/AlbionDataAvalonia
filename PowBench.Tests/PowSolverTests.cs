using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlbionDataAvalonia.Network.Pow;
using Xunit;

namespace PowBench.Tests;

public class PowSolverTests
{
    public static IEnumerable<object[]> Variants => SolverVariants.All.Select(variant => new object[] { variant.Name });

    [Theory]
    [InlineData(0UL, "0000000000000000")]
    [InlineData(9UL, "0000000000000009")]
    [InlineData(10UL, "000000000000000a")]
    [InlineData(15UL, "000000000000000f")]
    [InlineData(16UL, "0000000000000010")]
    [InlineData(0x0123456789abcdefUL, "0123456789abcdef")]
    [InlineData(ulong.MaxValue, "ffffffffffffffff")]
    public void FormatsCounterAsSixteenLowercaseHexDigits(ulong counter, string expected)
    {
        Span<byte> buffer = stackalloc byte[16];
        PowSolver.WriteCounterHex(buffer, counter);
        Assert.Equal(expected, Encoding.ASCII.GetString(buffer));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void AdvancesCounterAcrossDigitChangesCarriesAndWraparound(string variant)
    {
        using var solver = CreateSolver(variant);
        ulong[] counters = [0, 8, 9, 14, 15, 0xff, 0xffff, 0x0123456789abcdef, ulong.MaxValue];
        foreach (ulong counter in counters)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(counter.ToString("x16", CultureInfo.InvariantCulture));
            ulong next = unchecked(counter + 1);
            solver.AdvanceCounter(buffer, next);
            Assert.Equal(next.ToString("x16", CultureInfo.InvariantCulture), Encoding.ASCII.GetString(buffer));
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void HashesKnownSha256Vector(string variant)
    {
        using var solver = CreateSolver(variant);
        Span<byte> hash = stackalloc byte[32];
        // Repeated calls also exercise reuse of the instance-based hasher.
        for (int i = 0; i < 2; i++)
        {
            solver.TryComputeHash("abc"u8, hash);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Convert.ToHexStringLower(hash));
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void MatchesBitsOfLowercaseHexTextIncludingPartialBytes(string variant)
    {
        using var solver = CreateSolver(variant);
        byte[] hash = SHA256.HashData("abc"u8);
        string bits = ToAsciiBits(Convert.ToHexStringLower(hash));

        // All 512 prefix lengths cover partial bytes, odd nibbles and the full digest.
        for (int length = 0; length <= bits.Length; length++)
        {
            string wanted = bits[..length];
            Assert.True(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(wanted)));
            if (length > 0)
            {
                string mismatch = wanted[..^1] + (wanted[^1] == '0' ? '1' : '0');
                Assert.False(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(mismatch)));
            }
        }

        Assert.True(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create(null)));
        // The first raw hash byte is 0xba, but the first ASCII byte is 'b' (0x62).
        Assert.False(solver.CheckLeadingBits(hash, PowSolver.PowDifficulty.Create("1011")));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void FindsFirstSolutionForKnownChallenge(string variant)
    {
        using var solver = CreateSolver(variant);
        var challenge = new PowRequest { Key = "test-key", Wanted = "01100011011000110011100" };

        string solution = solver.ProcessPow(challenge);

        Assert.Equal("0000000000000200", solution);
        Assert.StartsWith(challenge.Wanted, HashBits(solution, challenge.Key));
        Assert.Equal("0000000000000201", solver.ProcessPow(new PowRequest { Key = "next", Wanted = "" }));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task AsyncSolveSupportsUtf8KeysAndNonzeroStartingCounters(string variant)
    {
        const string key = "Albion-ação-雪";
        const ulong start = 32;
        string wanted = HashBits("0000000000000040", key)[..23];
        // Independent, bounded string-based search; counter 64 is known to match.
        ulong expected = start;
        while (expected < 64 && !HashBits(expected.ToString("x16"), key).StartsWith(wanted, StringComparison.Ordinal))
        {
            expected++;
        }

        using var solver = CreateSolver(variant);
        solver.ResetCounter(start);
        string solution = await solver.SolvePow(new PowRequest { Key = key, Wanted = wanted });

        Assert.Equal(expected.ToString("x16"), solution);
        Assert.StartsWith(wanted, HashBits(solution, key));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void EmptyDifficultyAcceptsStartingCounterAndSuccessWrapsToZero(string variant)
    {
        using var solver = CreateSolver(variant);
        solver.ResetCounter(ulong.MaxValue);
        var challenge = new PowRequest { Key = "test-key", Wanted = "" };

        Assert.Equal("ffffffffffffffff", solver.ProcessPow(challenge));
        Assert.Equal("0000000000000000", solver.ProcessPow(challenge));
        Assert.Equal("0000000000000001", solver.ProcessPow(challenge));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void SearchWrapsToZeroAfterRejectedMaximumCounter(string variant)
    {
        const string key = "test-key";
        string wanted = HashBits("0000000000000000", key)[..23];
        Assert.False(HashBits("ffffffffffffffff", key).StartsWith(wanted, StringComparison.Ordinal));
        using var solver = CreateSolver(variant);
        solver.ResetCounter(ulong.MaxValue);

        Assert.Equal("0000000000000000", solver.ProcessPow(new PowRequest { Key = key, Wanted = wanted }));
    }

    private static PowSolver CreateSolver(string name) => SolverVariants.All.Single(variant => variant.Name == name).Create();

    private static string HashBits(string counter, string key) =>
        ToAsciiBits(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"aod^{counter}^{key}"))));

    private static string ToAsciiBits(string hex) =>
        string.Concat(hex.Select(character => Convert.ToString(character, 2).PadLeft(8, '0')));
}
