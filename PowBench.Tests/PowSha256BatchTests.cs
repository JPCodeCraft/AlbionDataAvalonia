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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(17)]
    [InlineData(33)]
    [InlineData(34)]
    public void EveryLaneMatchesSha256AcrossCounterCarriesAndWraparound(int keyLength)
    {
        string key = new string('x', keyLength);
        var batch = new PowSha256Batch(Encoding.UTF8.GetBytes($"aod^0000000000000000^{key}"));
        ulong[] counters = [0, 1, 7, 9, 14, 0xfc, 0xfffd, 0x10000, uint.MaxValue - 3UL, ulong.MaxValue - 3, ulong.MaxValue];
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

    [Fact]
    public void EveryLaneMatchesSha256ForRandomKeysAndCounters()
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
            var batch = new PowSha256Batch(Encoding.UTF8.GetBytes($"aod^0000000000000000^{key}"));
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
