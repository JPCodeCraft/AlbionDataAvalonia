using AlbionData.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Pow;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class Uploader : IDisposable
{
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    private PlayerState _playerState;
    private PowSolver _powSolver;
    private ConnectionService _connectionService;
    private SettingsManager _settingsManager;

    private readonly string marketOrdersIngestSubject = "marketorders.ingest";
    private readonly string marketHistoriesIngestSubject = "markethistories.ingest";
    private readonly string goldDataIngestSubject = "goldprices.ingest";

    public event EventHandler<MarketUploadEventArgs> OnMarketUpload;
    public event EventHandler<GoldPriceUploadEventArgs> OnGoldPriceUpload;
    public event EventHandler<MarketHistoriesUploadEventArgs> OnMarketHistoryUpload;
    public Uploader(PlayerState playerState, PowSolver powSolver, ConnectionService connectionService, SettingsManager settingsManager)
    {
        _playerState = playerState;
        _powSolver = powSolver;
        _connectionService = connectionService;
        _settingsManager = settingsManager;

        OnGoldPriceUpload += _playerState.GoldPriceUploadHandler;
        OnMarketUpload += _playerState.MarketUploadHandler;
        OnMarketHistoryUpload += _playerState.MarketHistoryUploadHandler;
    }
    public async Task Upload(MarketUpload marketUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        _playerState.UploadQueueSize++;
        var offers = marketUpload.Orders.Where(x => x.AuctionType == "offer").Count();
        var requests = marketUpload.Orders.Where(x => x.AuctionType == "request").Count();
        var data = SerializeData(marketUpload);
        if (await UploadData(data, _playerState.AlbionServer, marketOrdersIngestSubject))
        {
            OnMarketUpload?.Invoke(this, new MarketUploadEventArgs(marketUpload, _playerState.AlbionServer));
        }
        _playerState.UploadQueueSize--;
    }
    public async Task Upload(GoldPriceUpload goldHistoryUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        _playerState.UploadQueueSize++;
        var amount = goldHistoryUpload.Prices.Length;
        var data = SerializeData(goldHistoryUpload);

        if (await UploadData(data, _playerState.AlbionServer, goldDataIngestSubject))
        {
            OnGoldPriceUpload?.Invoke(this, new GoldPriceUploadEventArgs(goldHistoryUpload, _playerState.AlbionServer));
        }
        _playerState.UploadQueueSize--;
    }
    public async Task Upload(MarketHistoriesUpload marketHistoriesUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        _playerState.UploadQueueSize++;
        var count = marketHistoriesUpload.MarketHistories.Count;
        var timescale = marketHistoriesUpload.Timescale;
        var data = SerializeData(marketHistoriesUpload);

        if (await UploadData(data, _playerState.AlbionServer, marketHistoriesIngestSubject))
        {
            OnMarketHistoryUpload?.Invoke(this, new MarketHistoriesUploadEventArgs(marketHistoriesUpload, _playerState.AlbionServer));
        }
        _playerState.UploadQueueSize--;
    }
    private byte[] SerializeData(object upload)
    {
        return JsonSerializer.SerializeToUtf8Bytes(upload, new JsonSerializerOptions { IncludeFields = true });
    }

    private string GetHash(byte[] data, AlbionServer server)
    {
        var serverBytes = SerializeData(server);
        var combinedBytes = new byte[data.Length + serverBytes.Length];
        Buffer.BlockCopy(data, 0, combinedBytes, 0, data.Length);
        Buffer.BlockCopy(serverBytes, 0, combinedBytes, data.Length, serverBytes.Length);

        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(combinedBytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return hash;
        }
    }
    private async Task<bool> UploadData(byte[] data, AlbionServer server, string topic)
    {
        try
        {
            string dataHash = GetHash(data, server);
            if (_playerState.CheckHashInQueue(dataHash))
            {
                Log.Verbose("Data hash is already in queue, skipping uload");
                return false;
            }
            _playerState.AddSentDataHash(dataHash);

            await semaphore.WaitAsync();

            try
            {
                var powRequest = await _powSolver.GetPowRequest(server, _connectionService.httpClient);
                if (powRequest is not null)
                {
                    var solution = await _powSolver.SolvePow(powRequest, _settingsManager.UserSettings.ThreadLimitPercentage);
                    if (!string.IsNullOrEmpty(solution))
                    {
                        await UploadWithPow(powRequest, solution, data, topic, server, _connectionService.httpClient);
                        return true;
                    }
                    else
                    {
                        Log.Error("PoW solution is null or empty.");
                        return false;
                    }
                }
                else
                {
                    Log.Error("PoW request is null.");
                    return false;
                }
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading data to {0}.", server.Name);
            return false;
        }

    }
    private async Task UploadWithPow(PowRequest pow, string solution, byte[] data, string topic, AlbionServer server, HttpClient client)
    {
        if (client.BaseAddress == null)
        {
            Log.Error("Base address is null.");
            return;
        }

        var dataToSend = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("key", pow.Key),
            new KeyValuePair<string, string>("solution", solution),
            new KeyValuePair<string, string>("serverid", server.Id.ToString()),
            new KeyValuePair<string, string>("natsmsg", Encoding.UTF8.GetString(data)),
        });

        var requestUri = new Uri(client.BaseAddress, "/pow/" + topic);
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Content = dataToSend;

        HttpResponseMessage response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Log.Error("HTTP Error while proving pow. Returned: {0} ({1})", response.StatusCode, await response.Content.ReadAsStringAsync());
            return;
        }

        Log.Debug("Successfully sent ingest request to {0}", requestUri);
        return;
    }

    public void Dispose()
    {
        OnGoldPriceUpload -= _playerState.GoldPriceUploadHandler;
        OnMarketUpload -= _playerState.MarketUploadHandler;
        OnMarketHistoryUpload -= _playerState.MarketHistoryUploadHandler;
        Log.Information("Uploader disposed.");
    }
}
