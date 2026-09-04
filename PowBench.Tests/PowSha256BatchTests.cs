using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;
using AlbionDataAvalonia.Network.Pow;
using Xunit;

namespace PowBench.Tests;

public class PowSha256BatchTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(17, false)]
    [InlineData(17, true)]
    [InlineData(33, false)]
    [InlineData(33, true)]
    [InlineData(34, false)]
    [InlineData(34, true)]
    public void EveryLaneMatchesSha256AcrossCounterCarriesAndWraparound(int keyLength, bool precompute)
    {
        string key = new string('x', keyLength);
        var batch = new PowSha256Batch(Encoding.UTF8.GetBytes($"aod^0000000000000000^{key}"), precompute);
        // Revisit prefixes after mixed-prefix batches as well as after ordinary cache hits.
        ulong[] counters = [0, 1, 7, 9, 14, 0xfc, 0xfff8, 0xfff9, 0, 0x10000, 0x10008, 0,
            uint.MaxValue - 7UL, uint.MaxValue - 3UL, 0, (ulong)uint.MaxValue + 1,
            ulong.MaxValue - 7, ulong.MaxValue - 3, ulong.MaxValue, 0];
        Span<Vector256<uint>> digest = stackalloc Vector256<uint>[8];
        Span<byte> actual = stackalloc byte[32];
        foreach (ulong counter in counters)
        {
            batch.Hash(counter, digest);
            for (int lane = 0; lane < 8; lane++)
            {
                for (int word = 0; word < 8; word++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(actual[(word * 4)..], digest[word].GetElement(lane));
                }

                string nonce = unchecked(counter + (ulong)lane).ToString("x16", CultureInfo.InvariantCulture);
                byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes($"aod^{nonce}^{key}"));
                Assert.Equal(expected, actual.ToArray());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryLaneMatchesSha256ForRandomKeysAndCounters(bool precompute)
    {
        var random = new Random(1804);
        Span<Vector256<uint>> digest = stackalloc Vector256<uint>[8];
        Span<byte> actual = stackalloc byte[32];
        var keyBytes = new byte[17];
        var counterBytes = new byte[8];
        for (int sample = 0; sample < 128; sample++)
        {
            random.NextBytes(keyBytes);
            random.NextBytes(counterBytes);
            string key = Convert.ToHexStringLower(keyBytes)[..(sample % 35)];
            ulong counter = BinaryPrimitives.ReadUInt64LittleEndian(counterBytes);
            var batch = new PowSha256Batch(Encoding.UTF8.GetBytes($"aod^0000000000000000^{key}"), precompute);
            batch.Hash(counter, digest);
            for (int lane = 0; lane < 8; lane++)
            {
                for (int word = 0; word < 8; word++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(actual[(word * 4)..], digest[word].GetElement(lane));
                }

                string nonce = unchecked(counter + (ulong)lane).ToString("x16", CultureInfo.InvariantCulture);
                Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes($"aod^{nonce}^{key}")), actual.ToArray());
            }
        }
    }
}
