using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace AlbionDataAvalonia.Network.Pow;

// SHA-256, FIPS 180-4 section 6.2, with one independent counter per vector lane.
// Specialized for the single-block aod^<16 hex digits>^<key> message.
internal sealed class PowSha256Batch
{
    internal const int MaxInputLength = 55;
    internal static bool IsSupported => Avx2.IsSupported;

    private readonly Vector256<uint>[] words = new Vector256<uint>[64];

    private static ReadOnlySpan<uint> RoundConstants =>
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    ];

    internal PowSha256Batch(ReadOnlySpan<byte> input)
    {
        if (input.Length is < 21 or > MaxInputLength)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        Span<byte> block = stackalloc byte[64];
        block.Clear();
        input.CopyTo(block);
        block[input.Length] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(block[56..], (ulong)input.Length * 8);
        for (int i = 0; i < 16; i++)
        {
            words[i] = Vector256.Create(BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]));
        }
    }

    internal void Hash(ulong counter, Span<Vector256<uint>> digest)
    {
        // Reuse the schedule; only the four counter words change between batches.
        Span<Vector256<uint>> w = words;
        var start = Vector256.Create(unchecked((uint)counter));
        var low = start + Vector256.Create(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
        var carry = Vector256.LessThan(low, start) & Vector256.Create(1u);
        var high = Vector256.Create((uint)(counter >> 32)) + carry;
        w[1] = HexWord(high >> 16);
        w[2] = HexWord(high);
        w[3] = HexWord(low >> 16);
        w[4] = HexWord(low);

        for (int i = 16; i < 64; i++)
        {
            Vector256<uint> x = w[i - 15];
            Vector256<uint> y = w[i - 2];
            var s0 = RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
            var s1 = RotateRight(y, 17) ^ RotateRight(y, 19) ^ (y >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        var a = Vector256.Create(0x6a09e667u);
        var b = Vector256.Create(0xbb67ae85u);
        var c = Vector256.Create(0x3c6ef372u);
        var d = Vector256.Create(0xa54ff53au);
        var e = Vector256.Create(0x510e527fu);
        var f = Vector256.Create(0x9b05688cu);
        var g = Vector256.Create(0x1f83d9abu);
        var h = Vector256.Create(0x5be0cd19u);
        for (int i = 0; i < 64; i++)
        {
            var sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
            var choose = g ^ (e & (f ^ g));
            var temp1 = h + sum1 + choose + Vector256.Create(RoundConstants[i]) + w[i];
            var sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
            var majority = (a & b) | (c & (a | b));
            var temp2 = sum0 + majority;
            h = g;
            g = f;
            f = e;
            e = d + temp1;
            d = c;
            c = b;
            b = a;
            a = temp1 + temp2;
        }

        digest[0] = a + Vector256.Create(0x6a09e667u);
        digest[1] = b + Vector256.Create(0xbb67ae85u);
        digest[2] = c + Vector256.Create(0x3c6ef372u);
        digest[3] = d + Vector256.Create(0xa54ff53au);
        digest[4] = e + Vector256.Create(0x510e527fu);
        digest[5] = f + Vector256.Create(0x9b05688cu);
        digest[6] = g + Vector256.Create(0x1f83d9abu);
        digest[7] = h + Vector256.Create(0x5be0cd19u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> RotateRight(Vector256<uint> value, int bits) =>
        (value >> bits) | (value << (32 - bits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<uint> HexWord(Vector256<uint> value)
    {
        var nibbles = ((value & Vector256.Create(0xf000u)) << 12)
            | ((value & Vector256.Create(0x0f00u)) << 8)
            | ((value & Vector256.Create(0x00f0u)) << 4)
            | (value & Vector256.Create(0x000fu));
        var letters = Vector256.GreaterThan(nibbles.AsSByte(), Vector256.Create((sbyte)9)).AsUInt32();
        return nibbles + Vector256.Create(0x30303030u) + (letters & Vector256.Create(0x27272727u));
    }
}
