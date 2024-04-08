using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Pow;

public class PowSolver
{
    private static readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();

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

    // Generates a random hex string e.g.: faa2743d9181dca5
    private string RandomHex(int n)
    {
        byte[] bytes = new byte[n];

        rng.GetBytes(bytes);
        StringBuilder result = new StringBuilder(n * 2);
        foreach (byte b in bytes)
        {
            result.Append(b.ToString("x2"));
        }
        return result.ToString();
    }

    // Converts a hexadecimal string to a binary string e.g.: 0110011...
    private string ToBinaryBytes(string s)
    {
        StringBuilder buffer = new StringBuilder(s.Length * 8);
        foreach (char c in s)
        {
            buffer.Append(Convert.ToString(c, 2).PadLeft(8, '0'));
        }
        return buffer.ToString();
    }

    // Solves a pow looping through possible solutions
    // until a correct one is found
    // returns the solution
    public async Task<string> SolvePow(PowRequest pow, float threadLimitPercentage)
    {
        string solution = "";
        int threadLimit = Math.Max(1, (int)(Environment.ProcessorCount * threadLimitPercentage));
        var tasks = new List<Task<string>>();
        var tokenSource = new CancellationTokenSource();
        CancellationToken ct = tokenSource.Token;
        for (int i = 0; i < threadLimit; i++)
        {
            tasks.Add(Task.Run(() => ProcessPow(pow, ct), ct));
        }

        solution = await Task.WhenAny(tasks).Result;
        tokenSource.Cancel();

        return solution;
    }

    private string ProcessPow(PowRequest pow, CancellationToken token)
    {
        while (true)
        {
            string randhex = RandomHex(8);
            string hash = ToBinaryBytes(GetHash("aod^" + randhex + "^" + pow.Key));
            if (hash.StartsWith(pow.Wanted))
            {
                return randhex;
            }
            if (token.IsCancellationRequested)
            {
                Log.Verbose("Canceled PoW async task because of {token}.", nameof(token.IsCancellationRequested));
                return "";
            }
        }
    }

    private string GetHash(string input)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(new MemoryStream(bytes));
            StringBuilder builder = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }

}
