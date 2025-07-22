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

namespace AlbionDataAvalonia.Network.Pow;

public class PowSolver
{
    private readonly SHA256 _sha256 = SHA256.Create();
    private ulong _counter;
    private static readonly byte[] HexDigits = "0123456789abcdef"u8.ToArray();

    public PowSolver()
    {
        // Initialize with random starting point to avoid solver overlap
        byte[] randomBytes = new byte[8];
        Random.Shared.NextBytes(randomBytes);
        _counter = BitConverter.ToUInt64(randomBytes);
    }

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

    private string ProcessPow(PowRequest pow)
    {
        // Precompute constant components
        ReadOnlySpan<byte> prefix = "aod^"u8;
        byte[] suffix = Encoding.UTF8.GetBytes($"^{pow.Key}");
        int totalLength = prefix.Length + 16 + suffix.Length;

        // Buffer for hash input
        byte[] inputBuffer = new byte[totalLength];

        // Copy fixed components
        prefix.CopyTo(inputBuffer);
        suffix.CopyTo(inputBuffer.AsSpan(prefix.Length + 16));

        // Hash output buffer
        byte[] hashBuffer = new byte[32];

        while (true)
        {
            // Write current counter as hex to buffer
            WriteCounterHex(inputBuffer, prefix.Length, _counter++);

            // Compute hash
            _sha256.TryComputeHash(inputBuffer, hashBuffer, out _);

            // Check if hash meets difficulty
            if (CheckLeadingBits(hashBuffer, pow.Wanted))
            {
                return Encoding.ASCII.GetString(inputBuffer, prefix.Length, 16);
            }
        }
    }

    private void WriteCounterHex(byte[] buffer, int offset, ulong value)
    {
        // Write 16 hex digits (8 bytes) in big-endian order
        for (int i = 0; i < 16; i++)
        {
            // Process 4 bits at a time (from high to low)
            int nibble = (int)((value >> (60 - i * 4)) & 0xF);
            buffer[offset + i] = HexDigits[nibble];
        }
    }

    private bool CheckLeadingBits(byte[] hash, string wanted)
    {
        int totalBits = wanted.Length;
        int totalBytes = (totalBits + 7) / 8;
        Span<byte> hexChars = stackalloc byte[totalBytes];

        // Generate first N hex characters needed for comparison
        for (int i = 0; i < totalBytes; i++)
        {
            int byteIdx = i / 2;
            byte b = hash[byteIdx];
            int nibble = (i % 2 == 0) ? b >> 4 : b & 0x0F;
            hexChars[i] = HexDigits[nibble];
        }

        // Compare bits
        for (int i = 0; i < totalBits; i++)
        {
            int charIdx = i / 8;
            int bitIdx = 7 - (i % 8);  // MSB first

            byte current = hexChars[charIdx];
            int currentBit = (current >> bitIdx) & 1;
            int expectedBit = wanted[i] == '1' ? 1 : 0;

            if (currentBit != expectedBit)
            {
                return false;
            }
        }

        return true;
    }
}