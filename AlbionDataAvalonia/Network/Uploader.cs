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
    private readonly SemaphoreSlim semaphore;

    private PlayerState _playerState;
    private ConnectionService _connectionService;
    private SettingsManager _settingsManager;

    public event EventHandler<MarketUploadEventArgs> OnMarketUpload;
    public event EventHandler<GoldPriceUploadEventArgs> OnGoldPriceUpload;
    public event EventHandler<MarketHistoriesUploadEventArgs> OnMarketHistoryUpload;
    public Uploader(PlayerState playerState, ConnectionService connectionService, SettingsManager settingsManager)
    {
        _playerState = playerState;
        _connectionService = connectionService;
        _settingsManager = settingsManager;

        int maxThreads = Math.Max(1, (int)(Environment.ProcessorCount * _settingsManager.UserSettings.ThreadLimitPercentage));
        semaphore = new SemaphoreSlim(maxThreads, maxThreads);

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
        try
        {
            var offers = marketUpload.Orders.Where(x => x.AuctionType == "offer").Count();
            var requests = marketUpload.Orders.Where(x => x.AuctionType == "request").Count();
            var data = SerializeData(marketUpload);
            if (await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketOrdersIngestSubject ?? ""))
            {
                OnMarketUpload?.Invoke(this, new MarketUploadEventArgs(marketUpload, _playerState.AlbionServer));
            }
            _playerState.UploadQueueSize--;
        }
        catch (Exception ex)
        {
            _playerState.UploadQueueSize--;
            Log.Error(ex, "Exception while uploading market data.");
        }
    }
    public async Task Upload(GoldPriceUpload goldHistoryUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        _playerState.UploadQueueSize++;
        try
        {
            var amount = goldHistoryUpload.Prices.Length;
            var data = SerializeData(goldHistoryUpload);

            if (await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.GoldDataIngestSubject ?? ""))
            {
                OnGoldPriceUpload?.Invoke(this, new GoldPriceUploadEventArgs(goldHistoryUpload, _playerState.AlbionServer));
            }
            _playerState.UploadQueueSize--;
        }
        catch (Exception ex)
        {
            _playerState.UploadQueueSize--;
            Log.Error(ex, "Exception while uploading gold data.");
        }
    }
    public async Task Upload(MarketHistoriesUpload marketHistoriesUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        _playerState.UploadQueueSize++;
        try
        {
            var count = marketHistoriesUpload.MarketHistories.Count;
            var timescale = marketHistoriesUpload.Timescale;
            var data = SerializeData(marketHistoriesUpload);

            if (await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketHistoriesIngestSubject ?? ""))
            {
                OnMarketHistoryUpload?.Invoke(this, new MarketHistoriesUploadEventArgs(marketHistoriesUpload, _playerState.AlbionServer));
            }
            _playerState.UploadQueueSize--;
        }
        catch (Exception ex)
        {
            _playerState.UploadQueueSize--;
            Log.Error(ex, "Exception while uploading market history data.");
        }
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
                var _powSolver = new PowSolver();
                var powRequest = await _powSolver.GetPowRequest(server, _connectionService.httpClient);
                if (powRequest is not null)
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var solution = _powSolver.SolvePow(powRequest);
                    stopwatch.Stop();
                    _playerState.AddPowSolveTime(stopwatch.ElapsedMilliseconds);

                    Log.Debug("Solved PoW {key} with solution {solution} in {time} ms. ThreadLimitPercentage = {cores} of threads.", powRequest.Key, solution, stopwatch.ElapsedMilliseconds.ToString(), _settingsManager.UserSettings.ThreadLimitPercentage.ToString("P0"));

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
        semaphore.Dispose();
        OnGoldPriceUpload -= _playerState.GoldPriceUploadHandler;
        OnMarketUpload -= _playerState.MarketUploadHandler;
        OnMarketHistoryUpload -= _playerState.MarketHistoryUploadHandler;
        Log.Information("Uploader disposed.");
    }
}
