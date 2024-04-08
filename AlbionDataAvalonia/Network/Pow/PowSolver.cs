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
    private SHA256 sha256 = SHA256.Create();
    private static readonly string[] byteToBinaryLookup = Enumerable.Range(0, 256).Select(i => Convert.ToString(i, 2).PadLeft(8, '0')).ToArray();
    private static readonly string[] byteToHexLookup = Enumerable.Range(0, 256).Select(i => i.ToString("x2")).ToArray();

    private int counter = 0;

    public PowSolver()
    {
    }

    public async Task<PowRequest?> GetPowRequest(AlbionServer server, HttpClient client)
    {
        if (client.BaseAddress == null)
        {
            Log.Error("Base address is null.");
            return null;
        }

        var requestUri = new Uri(client.BaseAddress, "/pow");
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        HttpResponseMessage response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Log.Error("Got bad response code when getting PoW: {0}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        var powRequest = JsonSerializer.Deserialize<PowRequest>(content);
        if (powRequest is not null)
        {
            return powRequest;
        }
        else
        {
            return null;
        }
    }

    private string SequentialHex(int n)
    {
        return (++counter).ToString("x").PadLeft(n * 2, '0');
    }

    private string ToBinaryBytes(string s)
    {
        StringBuilder buffer = new StringBuilder(s.Length * 8);
        foreach (char c in s)
        {
            buffer.Append(byteToBinaryLookup[(byte)c]);
        }
        return buffer.ToString();
    }

    // Solves a pow looping through possible solutions
    // until a correct one is found
    // returns the solution
    public string SolvePow(PowRequest pow)
    {
        return ProcessPow(pow);
    }

    private string ProcessPow(PowRequest pow)
    {
        while (true)
        {
            var hex = SequentialHex(8);
            string hash = ToBinaryBytes(GetHash($"aod^{hex}^{pow.Key}"));
            if (hash.StartsWith(pow.Wanted, StringComparison.Ordinal))
            {
                return hex;
            }
        }
    }

    private string GetHash(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = sha256.ComputeHash(bytes);
        StringBuilder builder = new StringBuilder(hashBytes.Length * 2);
        foreach (byte b in hashBytes)
        {
            builder.Append(byteToHexLookup[b]);
        }
        return builder.ToString();
    }

}
