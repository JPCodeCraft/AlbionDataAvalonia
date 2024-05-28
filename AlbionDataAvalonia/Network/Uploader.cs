using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Pow;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Concurrent;
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
    private readonly ConcurrentQueue<Upload> uploadQueue = new();
    private readonly ConcurrentBag<Task> runningTasks = new();

    private PlayerState _playerState;
    private ConnectionService _connectionService;
    private SettingsManager _settingsManager;

    public event EventHandler<MarketUploadEventArgs> OnMarketUpload;
    public event EventHandler<GoldPriceUploadEventArgs> OnGoldPriceUpload;
    public event EventHandler<MarketHistoriesUploadEventArgs> OnMarketHistoryUpload;

    public event Action OnChange;

    public int uploadQueueCount => uploadQueue.Count;
    public int runningTasksCount => runningTasks.Count;

    public Uploader(PlayerState playerState, ConnectionService connectionService, SettingsManager settingsManager)
    {
        _playerState = playerState;
        _connectionService = connectionService;
        _settingsManager = settingsManager;

        OnGoldPriceUpload += _playerState.GoldPriceUploadHandler;
        OnMarketUpload += _playerState.MarketUploadHandler;
        OnMarketHistoryUpload += _playerState.MarketHistoryUploadHandler;
    }
    private async Task Upload(MarketUpload marketUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        try
        {
            var offers = marketUpload.Orders.Where(x => x.AuctionType == AuctionType.Offer).Count();
            var requests = marketUpload.Orders.Where(x => x.AuctionType == AuctionType.Request).Count();
            var data = SerializeData(marketUpload);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketOrdersIngestSubject ?? "");

            OnMarketUpload?.Invoke(this, new MarketUploadEventArgs(marketUpload, _playerState.AlbionServer, uploadStatus));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading market data.");
        }
    }
    private async Task Upload(GoldPriceUpload goldHistoryUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        try
        {
            var amount = goldHistoryUpload.Prices.Length;
            var data = SerializeData(goldHistoryUpload);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.GoldDataIngestSubject ?? "");

            OnGoldPriceUpload?.Invoke(this, new GoldPriceUploadEventArgs(goldHistoryUpload, _playerState.AlbionServer, uploadStatus));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading gold data.");
        }
    }
    private async Task Upload(MarketHistoriesUpload marketHistoriesUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        try
        {
            var count = marketHistoriesUpload.MarketHistories.Count;
            var timescale = marketHistoriesUpload.Timescale;
            var data = SerializeData(marketHistoriesUpload);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketHistoriesIngestSubject ?? "");

            OnMarketHistoryUpload?.Invoke(this, new MarketHistoriesUploadEventArgs(marketHistoriesUpload, _playerState.AlbionServer, uploadStatus));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading market history data.");
        }
    }
    private byte[] SerializeData(object upload)
    {
        return JsonSerializer.SerializeToUtf8Bytes(upload, new JsonSerializerOptions { IncludeFields = true });
    }

    public void EnqueueUpload(Upload upload)
    {
        uploadQueue.Enqueue(upload);
        OnChange?.Invoke();
    }

    private void RemoveCompletedTask(Task task)
    {
        runningTasks.TryTake(out task);
        OnChange?.Invoke();
    }

    public async Task ProcessItemsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            while (runningTasks.Count < _settingsManager.UserSettings.DesiredThreadCount && uploadQueue.TryDequeue(out var upload))
            {
                OnChange?.Invoke();
                if (upload.MarketUpload is not null)
                {
                    var uploadTask = Upload(upload.MarketUpload);
                    runningTasks.Add(uploadTask);
                    OnChange?.Invoke();
                    _ = uploadTask.ContinueWith(t => RemoveCompletedTask(t), TaskScheduler.Default);
                }
                if (upload.GoldPriceUpload is not null)
                {
                    var uploadTask = Upload(upload.GoldPriceUpload);
                    runningTasks.Add(uploadTask);
                    OnChange?.Invoke();
                    _ = uploadTask.ContinueWith(t => RemoveCompletedTask(t), TaskScheduler.Default);
                }
                if (upload.MarketHistoriesUpload is not null)
                {
                    var uploadTask = Upload(upload.MarketHistoriesUpload);
                    runningTasks.Add(uploadTask);
                    OnChange?.Invoke();
                    _ = uploadTask.ContinueWith(t => RemoveCompletedTask(t), TaskScheduler.Default);
                }
            }

            await Task.Delay(100, cancellationToken);
        }
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
    private async Task<UploadStatus> UploadData(byte[] data, AlbionServer server, string topic)
    {
        try
        {
            string dataHash = GetHash(data, server);
            if (_playerState.CheckHashInQueue(dataHash))
            {
                Log.Debug("Data hash is already in queue, skipping upload");
                return UploadStatus.Skipped;
            }
            _playerState.AddSentDataHash(dataHash);

            var _powSolver = new PowSolver();
            var powRequest = await _powSolver.GetPowRequest(server, _connectionService.httpClient);
            if (powRequest is not null)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var solution = await _powSolver.SolvePow(powRequest);
                stopwatch.Stop();
                _playerState.AddPowSolveTime(stopwatch.ElapsedMilliseconds);

                Log.Debug("Solved PoW {key} with solution {solution} in {time} ms.", powRequest.Key, solution, stopwatch.ElapsedMilliseconds.ToString());

                if (!string.IsNullOrEmpty(solution))
                {
                    if (await UploadWithPow(powRequest, solution, data, topic, server, _connectionService.httpClient))
                    {
                        return UploadStatus.Success;
                    }
                    else
                    {
                        return UploadStatus.Failed;
                    }
                }
                else
                {
                    Log.Error("PoW solution is null or empty.");
                    return UploadStatus.Failed;
                }
            }
            else
            {
                Log.Error("PoW request is null.");
                return UploadStatus.Failed;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading data to {0}.", server.Name);
            return UploadStatus.Failed;
        }

    }
    private async Task<bool> UploadWithPow(PowRequest pow, string solution, byte[] data, string topic, AlbionServer server, HttpClient client)
    {
        if (client.BaseAddress == null)
        {
            Log.Error("Failed to upload with Pow. Base address is null.");
            return false;
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
            return false;
        }

        Log.Debug("Successfully sent ingest request to {0}", requestUri);
        return true;
    }

    public void Dispose()
    {
        OnGoldPriceUpload -= _playerState.GoldPriceUploadHandler;
        OnMarketUpload -= _playerState.MarketUploadHandler;
        OnMarketHistoryUpload -= _playerState.MarketHistoryUploadHandler;
        Log.Information("Uploader disposed.");
    }
}
