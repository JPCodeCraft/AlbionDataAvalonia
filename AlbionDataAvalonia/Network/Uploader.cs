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
    private AFMUploader _afmUploader;

    public event EventHandler<MarketUploadEventArgs> OnMarketUpload;
    public event EventHandler<GoldPriceUploadEventArgs> OnGoldPriceUpload;
    public event EventHandler<MarketHistoriesUploadEventArgs> OnMarketHistoryUpload;
    public event EventHandler<BanditEventUploadEventArgs> OnBanditEventUpload;

    public event Action? OnChange;

    public int uploadQueueCount => uploadQueue.Count;
    public int runningTasksCount => runningTasks.Count;

    public Uploader(PlayerState playerState, ConnectionService connectionService, SettingsManager settingsManager, AFMUploader aFMUploader)
    {
        _playerState = playerState;
        _connectionService = connectionService;
        _settingsManager = settingsManager;
        _afmUploader = aFMUploader;

        OnGoldPriceUpload += _playerState.GoldPriceUploadHandler;
        OnMarketUpload += _playerState.MarketUploadHandler;
        OnMarketHistoryUpload += _playerState.MarketHistoryUploadHandler;
        OnBanditEventUpload += _playerState.BanditEventUploadHandler;
    }

    // MARK: Upload MarketUpload
    private async Task Upload(MarketUpload marketUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        try
        {
            if (_playerState.UploadToAfmOnly)
            {
                var afmUploadStatus = await _afmUploader.UploadMarketOrder(marketUpload);
                OnMarketUpload?.Invoke(this, new MarketUploadEventArgs(marketUpload, _playerState.AlbionServer, afmUploadStatus));

                if (afmUploadStatus == UploadStatus.Success)
                {
                    Log.Information("Market upload to AFM Flipper complete. {Offers} offers, {Requests} requests. Locations: {Location}",
                        marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer),
                        marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request),
                        string.Join(",", marketUpload.Orders.Select(x => x.Location.MarketLocation?.FriendlyName ?? "Unknown").Distinct()));
                }
                else
                {
                    Log.Error("Market upload to AFM Flipper receiver status {Status}. {Offers} offers, {Requests} requests.",
                        afmUploadStatus,
                        marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer),
                        marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request));
                }
            }

            var ordersForPublicUpload = new List<MarketOrder>();
            if (!_playerState.UploadToAfmOnly)
            {
                ordersForPublicUpload.AddRange(marketUpload.Orders);
            }
            else
            {
                ordersForPublicUpload.AddRange(marketUpload.Orders.Where(o =>
                    _settingsManager.AppSettings.ItemsToUploadToAfm?.Any(s => o.ItemTypeId.Contains(s)) == true));
            }

            if (ordersForPublicUpload.Any())
            {
                var publicMarketUpload = new MarketUpload();
                publicMarketUpload.Orders = ordersForPublicUpload;
                var data = SerializeData(publicMarketUpload);
                var offers = publicMarketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer);
                var requests = publicMarketUpload.Orders.Count(x => x.AuctionType == AuctionType.request);

                Log.Debug("Starting public upload of {Offers} offers, {Requests} requests. Identifier: {identifier}", offers, requests, publicMarketUpload.Identifier);

                var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketOrdersIngestSubject ?? "", publicMarketUpload.Identifier);

                OnMarketUpload?.Invoke(this, new MarketUploadEventArgs(publicMarketUpload, _playerState.AlbionServer, uploadStatus));

                if (uploadStatus == UploadStatus.Success)
                {
                    var serverName = _playerState.AlbionServer?.Name ?? string.Empty;
                    var serverLogger = Log.ForContext("server", serverName);
                    serverLogger.Information("Public market upload complete. {Offers} offers, {Requests} requests. Identifier: {identifier}. Locations: {Location}", offers, requests, publicMarketUpload.Identifier, string.Join(",", publicMarketUpload.Orders.Select(x => x.Location.MarketLocation?.FriendlyName ?? "Unknown").Distinct()));
                }
                else
                {
                    Log.Error("Public market upload received status {Status}. {Offers} offers, {Requests} requests. Identifier: {identifier}", uploadStatus, offers, requests, publicMarketUpload.Identifier);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading market data. Identifier: {identifier}", marketUpload.Identifier);
        }
    }

    // MARK: Upload GoldPriceUpload
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

            Log.Debug("Starting upload of gold data. {count} histories. Identifier: {identifier}", amount, goldHistoryUpload.Identifier);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.GoldDataIngestSubject ?? "", goldHistoryUpload.Identifier);

            OnGoldPriceUpload?.Invoke(this, new GoldPriceUploadEventArgs(goldHistoryUpload, _playerState.AlbionServer, uploadStatus));

            if (uploadStatus == UploadStatus.Success)
            {
                var serverName = _playerState.AlbionServer?.Name ?? string.Empty;
                var serverLogger = Log.ForContext("server", serverName);
                serverLogger.Information("Gold price upload complete. {count} histories. Identifier: {identifier}", amount, goldHistoryUpload.Identifier);
            }
            else
            {
                Log.Error("Gold price upload received status {Status}. {count} histories. Identifier: {identifier}", uploadStatus, amount, goldHistoryUpload.Identifier);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading gold data. Identifier: {identifier}", goldHistoryUpload.Identifier);
        }
    }

    // MARK: Upload BanditEventUpload
    private async Task Upload(BanditEventUpload banditEventUpload)
    {
        if (_playerState.AlbionServer == null)
        {
            Log.Error("Albion server is not set.");
            return;
        }
        try
        {
            var data = SerializeData(banditEventUpload);

            Log.Debug("Starting upload of bandit event. Phase {Phase} {EventTime}. Identifier: {identifier}", banditEventUpload.Phase, banditEventUpload.EventTime, banditEventUpload.Identifier);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.BanditEventIngestSubject ?? "", banditEventUpload.Identifier);

            OnBanditEventUpload?.Invoke(this, new BanditEventUploadEventArgs(banditEventUpload, _playerState.AlbionServer, uploadStatus));

            if (uploadStatus == UploadStatus.Success)
            {
                var serverName = _playerState.AlbionServer?.Name ?? string.Empty;
                var serverLogger = Log.ForContext("server", serverName);
                serverLogger.Information("Bandit event upload complete. Phase {Phase} {EventTime}. Identifier: {identifier}", banditEventUpload.Phase, banditEventUpload.EventTime, banditEventUpload.Identifier);
            }
            else
            {
                Log.Error("Bandit event upload received status {Status}. Phase {Phase} {EventTime}. Identifier: {identifier}", uploadStatus, banditEventUpload.Phase, banditEventUpload.EventTime, banditEventUpload.Identifier);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading bandit event data. Identifier: {identifier}", banditEventUpload.Identifier);
        }
    }

    // MARK: Upload MarketHistoriesUpload
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

            Log.Debug("Starting upload of market history. [{Timescale}] => {count} histories of {item}. Identifier: {identifier}", marketHistoriesUpload.Timescale, count, marketHistoriesUpload.AlbionId, marketHistoriesUpload.Identifier);

            var uploadStatus = await UploadData(data, _playerState.AlbionServer, _settingsManager.AppSettings.MarketHistoriesIngestSubject ?? "", marketHistoriesUpload.Identifier);

            OnMarketHistoryUpload?.Invoke(this, new MarketHistoriesUploadEventArgs(marketHistoriesUpload, _playerState.AlbionServer, uploadStatus));

            if (uploadStatus == UploadStatus.Success)
            {
                var serverName = _playerState.AlbionServer?.Name ?? string.Empty;
                var serverLogger = Log.ForContext("server", serverName);
                serverLogger.Information("Market history upload complete. [{Timescale}] => {count} histories of {item}. Identifier: {identifier}. Location: {Location}", marketHistoriesUpload.Timescale, count, marketHistoriesUpload.AlbionId, marketHistoriesUpload.Identifier, marketHistoriesUpload.Location.MarketLocation?.FriendlyName ?? "Unknown");
            }
            else
            {
                Log.Error("Market history upload received status {Status}. [{Timescale}] => {count} histories of {item}. Identifier: {identifier}", uploadStatus, marketHistoriesUpload.Timescale, count, marketHistoriesUpload.AlbionId, marketHistoriesUpload.Identifier);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading market history data. Identifier: {identifier}", marketHistoriesUpload.Identifier);
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
                if (upload.BanditEventUpload is not null)
                {
                    var uploadTask = Upload(upload.BanditEventUpload);
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
    private async Task<UploadStatus> UploadData(byte[] data, AlbionServer server, string topic, Guid identifier)
    {
        try
        {
            string dataHash = GetHash(data, server);
            if (_playerState.CheckHashInQueue(dataHash))
            {
                Log.Debug("Data hash is already in queue, skipping upload. Identifier: {identifier}", identifier);
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

                Log.Verbose("Solved PoW {key} with solution {solution} in {time} ms. Identifier: {identifier}", powRequest.Key, solution, stopwatch.ElapsedMilliseconds.ToString(), identifier);

                if (!string.IsNullOrEmpty(solution))
                {
                    if (await UploadWithPow(powRequest, solution, data, topic, server, _connectionService.httpClient, identifier))
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
                    Log.Error("PoW solution is null or empty. Identifier: {identifier}", identifier);
                    return UploadStatus.Failed;
                }
            }
            else
            {
                Log.Error("PoW request is null. Identifier: {identifier}", identifier);
                return UploadStatus.Failed;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while uploading data to {0}. Identifier: {identifier}", server.Name, identifier);
            return UploadStatus.Failed;
        }

    }
    private async Task<bool> UploadWithPow(PowRequest pow, string solution, byte[] data, string topic, AlbionServer server, HttpClient client, Guid identifier)
    {
        if (client.BaseAddress == null)
        {
            Log.Error("Failed to upload with Pow. Base address is null. Identifier: {identifier}", identifier);
            return false;
        }

        var dataToSend = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("key", pow.Key),
            new KeyValuePair<string, string>("solution", solution),
            new KeyValuePair<string, string>("serverid", server.Id.ToString()),
            new KeyValuePair<string, string>("natsmsg", Encoding.UTF8.GetString(data)),
            new KeyValuePair<string, string>("identifier", identifier.ToString())
        });

        var requestUri = new Uri(client.BaseAddress, "/pow/" + topic);
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Content = dataToSend;

        HttpResponseMessage response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Log.Error("HTTP Error while proving pow. Returned: {0} ({1}). Identifier: {identifier}", response.StatusCode, await response.Content.ReadAsStringAsync(), identifier);
            return false;
        }

        Log.Verbose("Successfully sent ingest request to {0}. Identifier: {identifier}", requestUri, identifier);
        return true;
    }

    public void Dispose()
    {
        OnGoldPriceUpload -= _playerState.GoldPriceUploadHandler;
        OnMarketUpload -= _playerState.MarketUploadHandler;
        OnMarketHistoryUpload -= _playerState.MarketHistoryUploadHandler;
        OnBanditEventUpload -= _playerState.BanditEventUploadHandler;
        Log.Information("Uploader disposed.");
    }
}
