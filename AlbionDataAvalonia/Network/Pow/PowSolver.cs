using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;


namespace AlbionDataAvalonia.Network.Pow;

public partial class PowSolver
{
    private readonly SHA256 _sha256 = SHA256.Create();
    private ulong _counter;
    private static readonly byte[] HexDigits = "0123456789abcdef"u8.ToArray();

    internal void ResetCounter(ulong value) => _counter = value;

    public async Task<PowRequest?> GetPowRequest(AlbionServer server, HttpClient client)
    {
        if (client.BaseAddress == null)
        {
            Log.Error("Base address is null.");
            return null;
        }

        var requestUri = new Uri(client.BaseAddress, "/pow");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            Log.Error("Got bad response code when getting PoW: {0}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PowRequest>(content);
    }

    public Task<string> SolvePow(PowRequest pow) => Task.Run(() => ProcessPow(pow));

    internal string ProcessPow(PowRequest pow)
    {
        ReadOnlySpan<byte> prefix = "aod^"u8;
        byte[] suffix = Encoding.UTF8.GetBytes($"^{pow.Key}");
        int totalLength = prefix.Length + 16 + suffix.Length;

        byte[] inputBuffer = new byte[totalLength];
        prefix.CopyTo(inputBuffer);
        suffix.CopyTo(inputBuffer.AsSpan(prefix.Length + 16));

        PowDifficulty difficulty = PowDifficulty.Create(pow.Wanted);
        Span<byte> counterSpan = inputBuffer.AsSpan(prefix.Length, 16);
        Span<byte> hashBuffer = stackalloc byte[32];

        // write once, then increment ASCII in-place each loop
        ulong ctr = _counter;
        WriteCounterHex(counterSpan, ctr);

        while (true)
        {
            TryComputeHash(inputBuffer, hashBuffer);

            if (CheckLeadingBits(hashBuffer, difficulty))
            {
                // keep original semantics where _counter has advanced by 1 after success
                _counter = ctr + 1;
                return Encoding.ASCII.GetString(counterSpan);
            }

            ctr++;
            IncrementHexAsciiInPlace(counterSpan); // O(1) on average
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void IncrementHexAsciiInPlace(Span<byte> s)
    {
        const byte ZERO = (byte)'0';
        const byte NINE = (byte)'9';
        const byte A = (byte)'a';
        const byte F = (byte)'f';

        // start from least-significant nibble (rightmost)
        for (int i = s.Length - 1; i >= 0; i--)
        {
            byte c = s[i];

            if (c == F) { s[i] = ZERO; continue; } // carry to next nibble
            if (c == NINE) { s[i] = A; break; }    // 9 -> a, no carry

            // '0'..'8' or 'a'..'e'
            s[i] = (byte)(c + 1);
            break;
        }
        // if we carried past the most-significant nibble, it naturally wraps to all '0's
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteCounterHex(Span<byte> dst, ulong value)
    {
        // write two hex chars per byte, most-significant first
        for (int i = 15; i >= 0; i -= 2)
        {
            byte b = (byte)(value & 0xFF);
            value >>= 8;
            dst[i - 1] = HexDigits[b >> 4];
            dst[i] = HexDigits[b & 0xF];
        }
    }


    internal void TryComputeHash(ReadOnlySpan<byte> input, Span<byte> hashBuffer) =>
        _sha256.TryComputeHash(input, hashBuffer, out _);
    // SHA256.TryHashData(input, hashBuffer, out +_);

    internal static bool CheckLeadingBits(ReadOnlySpan<byte> hash, PowDifficulty difficulty)
    {
        ReadOnlySpan<byte> expected = difficulty.ExpectedSpan;
        if (expected.Length == 0)
        {
            return true;
        }

        ReadOnlySpan<byte> masks = difficulty.MaskSpan;

        for (int i = 0; i < expected.Length; i++)
        {
            int byteIdx = i >> 1;
            byte source = hash[byteIdx];
            byte nibble = (i & 1) == 0 ? (byte)(source >> 4) : (byte)(source & 0x0F);
            byte ascii = HexDigits[nibble];

            if (((ascii ^ expected[i]) & masks[i]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    internal sealed class PowDifficulty
    {
        private static readonly PowDifficulty Empty = new PowDifficulty(Array.Empty<byte>(), Array.Empty<byte>());

        private readonly byte[] _expected;
        private readonly byte[] _mask;

        private PowDifficulty(byte[] expected, byte[] mask)
        {
            _expected = expected;
            _mask = mask;
        }

        public ReadOnlySpan<byte> ExpectedSpan => _expected;
        public ReadOnlySpan<byte> MaskSpan => _mask;

        public static PowDifficulty Create(string? wanted)
        {
            if (string.IsNullOrEmpty(wanted))
            {
                return Empty;
            }

            int bitLength = wanted.Length;
            int byteLength = (bitLength + 7) / 8;
            byte[] expected = new byte[byteLength];
            byte[] mask = new byte[byteLength];

            int globalBitIndex = 0;
            for (int i = 0; i < byteLength; i++)
            {
                int bitsInThisByte = Math.Min(8, bitLength - globalBitIndex);
                byte expectedByte = 0;
                byte maskByte = 0;

                for (int bit = 0; bit < bitsInThisByte; bit++, globalBitIndex++)
                {
                    int bitIdx = 7 - bit;
                    maskByte |= (byte)(1 << bitIdx);
                    if (wanted[globalBitIndex] == '1')
                    {
                        expectedByte |= (byte)(1 << bitIdx);
                    }
                }

                expected[i] = expectedByte;
                mask[i] = maskByte;
            }

            return new PowDifficulty(expected, mask);
        }
    }
}
