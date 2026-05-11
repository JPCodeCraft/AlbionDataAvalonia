using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class AFMUploader : IDisposable
    {
        private const string FlipperOrdersPath = "flipperOrders";
        private const string PlayerCountPath = "playercount";
        private const string AchievementsPath = "be/achievements";
        private const string GlobalMultiplierPath = "be/globalMultiplier";
        private const string ItemEstimatedMarketValuesPath = "itemEstimatedMarketValues";
        private const int MaxItemEstimatedMarketValueUploadBatchSize = 500;
        private const int MaxUploadedItemEstimatedMarketValueFingerprints = 5_000;
        private const int MaxUploadedGlobalMultiplierFingerprints = 10;
        private static readonly TimeSpan ItemEstimatedMarketValueBatchDelay = TimeSpan.FromSeconds(5);

        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;
        private readonly AuthService _authService;

        private readonly HttpClient httpClient = new HttpClient();
        private readonly ConcurrentDictionary<ItemEstimatedMarketValueUploadKey, ItemEstimatedMarketValueUploadEntry> pendingItemEstimatedMarketValues = new();
        private readonly BoundedUploadFingerprintCache<ItemEstimatedMarketValueUploadFingerprint> uploadedItemEstimatedMarketValues = new(MaxUploadedItemEstimatedMarketValueFingerprints);
        private readonly BoundedUploadFingerprintCache<GlobalMultiplierUploadFingerprint> uploadedGlobalMultipliers = new(MaxUploadedGlobalMultiplierFingerprints);

        private readonly object _headersLock = new();
        private readonly object itemEstimatedMarketValueUploadTimerLock = new();
        private Timer? itemEstimatedMarketValueUploadTimer;
        private bool itemEstimatedMarketValueUploadScheduled;
        private int itemEstimatedMarketValueUploadInProgress;
        private bool disposed;
        public event EventHandler<AchievementsUploadEventArgs>? OnAchievementsUpload;
        public event EventHandler<GlobalMultiplierUploadEventArgs>? OnGlobalMultiplierUpload;
        public event EventHandler<ItemEstimatedMarketValueUploadEventArgs>? OnItemEstimatedMarketValueUpload;

        public AFMUploader(PlayerState playerState, SettingsManager settingsManager, AuthService authService)
        {
            _playerState = playerState;
            _settingsManager = settingsManager;
            _authService = authService;

            OnAchievementsUpload += _playerState.AchievementsUploadHandler;
            OnGlobalMultiplierUpload += _playerState.GlobalMultiplierUploadHandler;
            OnItemEstimatedMarketValueUpload += _playerState.ItemEstimatedMarketValueUploadHandler;
            _authService.FirebaseUserChanged += (user) => UpdateAuthHeader(user);
        }

        private void UpdateAuthHeader(FirebaseAuthResponse? user)
        {
            lock (_headersLock)
            {
                try
                {
                    if (user is not null &&
                        !string.IsNullOrWhiteSpace(user.IdToken) &&
                        !string.IsNullOrWhiteSpace(user.LocalId))
                    {
                        var newAuth = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.IdToken);

                        // Only set if changed to reduce churn
                        if (!Equals(httpClient.DefaultRequestHeaders.Authorization, newAuth))
                        {
                            httpClient.DefaultRequestHeaders.Authorization = newAuth;
                        }

                        // Replace X-User-Id safely, avoiding validation exceptions
                        httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                        if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", user.LocalId))
                        {
                            Log.Warning("Failed to set X-User-Id header due to validation.");
                        }

                        Log.Debug("Set AFM upload auth header for user");
                    }
                    else
                    {
                        httpClient.DefaultRequestHeaders.Authorization = null;
                        httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                        Log.Debug("Cleared AFM upload auth header, since no user is logged in");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while updating AFM upload auth header; clearing headers as a fallback.");
                    httpClient.DefaultRequestHeaders.Authorization = null;
                    httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                }
            }
        }

        public void Initialize()
        {
            httpClient.BaseAddress = new Uri(_settingsManager.AppSettings.AfmTopItemsApiBase);
            httpClient.DefaultRequestHeaders.UserAgent.Clear();
            var version = AlbionDataAvalonia.ClientUpdater.GetVersion() ?? "unknown";
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"afmDataClient-v.{version}");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://github.com/JPCodeCraft/AlbionDataAvalonia");

            // Ensure we apply headers immediately if a session already exists.
            UpdateAuthHeader(_authService.CurrentFirebaseUser);
        }

        public async Task<UploadStatus> UploadMarketOrder(MarketUpload marketUpload)
        {
            var identifier = marketUpload.Identifier;
            var requestUri = httpClient.BaseAddress is null
                ? null
                : new Uri(httpClient.BaseAddress, $"{FlipperOrdersPath}?contributeToPublic={_playerState.ContributeToPublic}");

            try
            {
                if (_playerState.AlbionServer is null)
                {
                    Log.Error("Cannot upload market order without a server. Identifier: {Identifier}. Offers: {Offers}. Requests: {Requests}.", identifier, marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer), marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request));
                    return UploadStatus.Failed;
                }

                var hasValidToken = await _authService.EnsureValidTokenAsync();
                if (!hasValidToken)
                {
                    Log.Error("Cannot upload market order without a valid Firebase session. Identifier: {Identifier}. ServerId: {ServerId}. Offers: {Offers}. Requests: {Requests}.", identifier, _playerState.AlbionServer.Id, marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer), marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request));
                    return UploadStatus.Failed;
                }

                var firebaseUserId = _authService.FirebaseUserId;
                if (firebaseUserId is null)
                {
                    Log.Error("Cannot upload market order without a Firebase user ID. Identifier: {Identifier}. ServerId: {ServerId}. Offers: {Offers}. Requests: {Requests}.", identifier, _playerState.AlbionServer.Id, marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer), marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request));
                    return UploadStatus.Failed;
                }

                var afmMarketUpload = new AfmMarketUpload(marketUpload, _playerState.AlbionServer.Id, firebaseUserId);

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                JsonNode? jsonNode = JsonSerializer.SerializeToNode(afmMarketUpload, serializerOptions);

                if (jsonNode is JsonObject jsonObject && jsonObject["orders"] is JsonArray ordersArray)
                {
                    for (int i = 0; i < ordersArray.Count; i++)
                    {
                        var originalOrder = afmMarketUpload.Orders[i];
                        var orderNode = ordersArray[i]?.AsObject();
                        if (orderNode != null)
                        {
                            orderNode["locationId"] = originalOrder.Location.MarketLocation?.IdInt?.ToString() ?? "0";
                        }
                    }
                }

                if (requestUri is null)
                {
                    Log.Error("Cannot upload market order because AFM base address is not initialized. Identifier: {Identifier}. ServerId: {ServerId}. Offers: {Offers}. Requests: {Requests}.", identifier, _playerState.AlbionServer.Id, marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer), marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request));
                    return UploadStatus.Failed;
                }

                var payload = jsonNode?.ToJsonString(serializerOptions) ?? "{}";

                async Task<HttpResponseMessage> SendAsync()
                {
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    return await httpClient.PostAsync(requestUri, content);
                }

                HttpResponseMessage? response = null;

                try
                {
                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LogHttpFailure(
                            "market order",
                            requestUri,
                            identifier,
                            response,
                            "AFM market order upload returned unauthorized. Attempting token refresh.",
                            marketUploadSummary: GetMarketUploadSummary(marketUpload),
                            serverId: _playerState.AlbionServer.Id);

                        response.Dispose();
                        response = null;

                        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                        if (!recovered)
                        {
                            Log.Error("AFM market order upload could not recover from unauthorized response. Identifier: {Identifier}. ServerId: {ServerId}. {MarketUploadSummary}", identifier, _playerState.AlbionServer.Id, GetMarketUploadSummary(marketUpload));
                            return UploadStatus.Failed;
                        }

                        response = await SendAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            await LogHttpFailure(
                                "market order",
                                requestUri,
                                identifier,
                                response,
                                "AFM market order upload unauthorized after retry.",
                                marketUploadSummary: GetMarketUploadSummary(marketUpload),
                                serverId: _playerState.AlbionServer.Id);

                            return UploadStatus.Failed;
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await LogHttpFailure(
                            "market order",
                            requestUri,
                            identifier,
                            response,
                            "HTTP error while uploading market order to AFM.",
                            marketUploadSummary: GetMarketUploadSummary(marketUpload),
                            serverId: _playerState.AlbionServer.Id);

                        return UploadStatus.Failed;
                    }

                    Log.Debug("Successfully sent AfmMarketUpload to {RequestUri}. Identifier: {Identifier}. ServerId: {ServerId}. {MarketUploadSummary}", requestUri, identifier, _playerState.AlbionServer.Id, GetMarketUploadSummary(marketUpload));
                    return UploadStatus.Success;
                }
                finally
                {
                    response?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogAfmException(
                    ex,
                    "market order",
                    requestUri,
                    identifier,
                    marketUploadSummary: GetMarketUploadSummary(marketUpload),
                    serverId: _playerState.AlbionServer?.Id);
                return UploadStatus.Failed;
            }
        }

        public void UploadPlayerCount(PlayerCount playerCount)
        {
            _ = Upload(playerCount);
        }

        public void UploadAchievements(AchievementUpload achievementUpload)
        {
            _ = Upload(achievementUpload);
        }

        public void UploadGlobalMultiplier(GlobalMultiplierUpload globalMultiplierUpload)
        {
            var fingerprint = new GlobalMultiplierUploadFingerprint(globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
            if (uploadedGlobalMultipliers.Contains(fingerprint))
            {
                Log.Verbose("Skipping duplicate global multiplier upload. ServerId: {ServerId}. GlobalMultiplier: {GlobalMultiplier}.", globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
                return;
            }

            _ = Upload(globalMultiplierUpload, fingerprint);
        }

        public void QueueItemEstimatedMarketValue(string itemUniqueName, long emv, int quality)
        {
            var serverId = _playerState.AlbionServer?.Id;
            if (serverId is null)
            {
                Log.Debug("Skipping item estimated market value upload because server is not set. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", itemUniqueName, quality, emv);
                return;
            }

            if (_authService.CurrentFirebaseUser is null)
            {
                Log.Debug("Skipping item estimated market value upload because no Firebase session exists. ServerId: {ServerId}. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", serverId, itemUniqueName, quality, emv);
                return;
            }

            if (!IsValidItemEstimatedMarketValue(itemUniqueName, emv, quality))
            {
                Log.Debug("Skipping invalid item estimated market value. ServerId: {ServerId}. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", serverId, itemUniqueName, quality, emv);
                return;
            }

            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var fingerprint = new ItemEstimatedMarketValueUploadFingerprint(serverId.Value, itemUniqueName, quality, day, emv);
            if (uploadedItemEstimatedMarketValues.Contains(fingerprint))
            {
                Log.Verbose("Skipping duplicate item estimated market value upload. ServerId: {ServerId}. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}. Day: {Day}.", serverId.Value, itemUniqueName, quality, emv, day);
                return;
            }

            var key = new ItemEstimatedMarketValueUploadKey(serverId.Value, itemUniqueName, quality, day);
            pendingItemEstimatedMarketValues[key] = new ItemEstimatedMarketValueUploadEntry
            {
                ItemUniqueName = itemUniqueName,
                Emv = emv,
                Quality = quality,
                Day = day
            };

            ScheduleItemEstimatedMarketValueUpload();
        }

        private static bool IsValidItemEstimatedMarketValue(string itemUniqueName, long emv, int quality)
        {
            return !string.IsNullOrWhiteSpace(itemUniqueName)
                && !string.Equals(itemUniqueName, "Unknown Item", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(itemUniqueName, "Unset", StringComparison.OrdinalIgnoreCase)
                && emv > 0
                && quality >= 1
                && quality <= 5;
        }

        private void ScheduleItemEstimatedMarketValueUpload()
        {
            lock (itemEstimatedMarketValueUploadTimerLock)
            {
                if (disposed || itemEstimatedMarketValueUploadScheduled)
                {
                    return;
                }

                itemEstimatedMarketValueUploadScheduled = true;
                itemEstimatedMarketValueUploadTimer ??= new Timer(_ => _ = UploadPendingItemEstimatedMarketValues());
                itemEstimatedMarketValueUploadTimer.Change(ItemEstimatedMarketValueBatchDelay, Timeout.InfiniteTimeSpan);
            }
        }

        private async Task UploadPendingItemEstimatedMarketValues()
        {
            lock (itemEstimatedMarketValueUploadTimerLock)
            {
                itemEstimatedMarketValueUploadScheduled = false;
            }

            if (Interlocked.Exchange(ref itemEstimatedMarketValueUploadInProgress, 1) == 1)
            {
                ScheduleItemEstimatedMarketValueUpload();
                return;
            }

            try
            {
                var snapshot = pendingItemEstimatedMarketValues.ToArray();
                if (snapshot.Length == 0)
                {
                    return;
                }

                var items = new List<ItemEstimatedMarketValueUploadEntry>(snapshot.Length);
                int? serverId = null;

                foreach (var pair in snapshot)
                {
                    if (!pendingItemEstimatedMarketValues.TryRemove(pair.Key, out var entry))
                    {
                        continue;
                    }

                    serverId ??= pair.Key.ServerId;
                    if (serverId.Value != pair.Key.ServerId)
                    {
                        pendingItemEstimatedMarketValues[pair.Key] = entry;
                        continue;
                    }

                    items.Add(entry);
                }

                if (serverId is null || items.Count == 0)
                {
                    return;
                }

                foreach (var chunk in items.Chunk(MaxItemEstimatedMarketValueUploadBatchSize))
                {
                    foreach (var item in chunk)
                    {
                        Log.Debug("Uploading item estimated market value. ServerId: {ServerId}. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}. Day: {Day}.", serverId.Value, item.ItemUniqueName, item.Quality, item.Emv, item.Day);
                    }

                    var upload = new ItemEstimatedMarketValueUpload
                    {
                        ServerId = serverId.Value,
                        Items = chunk.ToList()
                    };

                    await Upload(upload);
                }

                if (!pendingItemEstimatedMarketValues.IsEmpty)
                {
                    ScheduleItemEstimatedMarketValueUpload();
                }
            }
            finally
            {
                Interlocked.Exchange(ref itemEstimatedMarketValueUploadInProgress, 0);
            }
        }

        private async Task<UploadStatus> Upload(ItemEstimatedMarketValueUpload itemEstimatedMarketValueUpload)
        {
            var identifier = itemEstimatedMarketValueUpload.Identifier;
            var requestUri = httpClient.BaseAddress is null ? null : new Uri(httpClient.BaseAddress, ItemEstimatedMarketValuesPath);

            UploadStatus ReportStatus(UploadStatus status)
            {
                OnItemEstimatedMarketValueUpload?.Invoke(this, new ItemEstimatedMarketValueUploadEventArgs(itemEstimatedMarketValueUpload, status, UploadScope.Private, identifier));
                return status;
            }

            try
            {
                var hasValidToken = await _authService.EnsureValidTokenAsync();
                if (!hasValidToken)
                {
                    Log.Error("Cannot upload item estimated market values without a valid Firebase session. Identifier: {Identifier}. ServerId: {ServerId}. ItemsCount: {ItemsCount}.", identifier, itemEstimatedMarketValueUpload.ServerId, itemEstimatedMarketValueUpload.Items.Count);
                    return ReportStatus(UploadStatus.Failed);
                }

                if (requestUri is null)
                {
                    Log.Error("Cannot upload item estimated market values because AFM base address is not initialized. Identifier: {Identifier}. ServerId: {ServerId}. ItemsCount: {ItemsCount}.", identifier, itemEstimatedMarketValueUpload.ServerId, itemEstimatedMarketValueUpload.Items.Count);
                    return ReportStatus(UploadStatus.Failed);
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                async Task<HttpResponseMessage> SendAsync()
                {
                    return await httpClient.PostAsJsonAsync(requestUri, itemEstimatedMarketValueUpload, serializerOptions);
                }

                HttpResponseMessage? response = null;

                try
                {
                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LogHttpFailure(
                            "item estimated market values",
                            requestUri,
                            identifier,
                            response,
                            "AFM item estimated market values upload returned unauthorized. Attempting token refresh.",
                            itemEstimatedMarketValueSummary: GetItemEstimatedMarketValueUploadSummary(itemEstimatedMarketValueUpload),
                            serverId: itemEstimatedMarketValueUpload.ServerId);

                        response.Dispose();
                        response = null;

                        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                        if (!recovered)
                        {
                            Log.Error("AFM item estimated market values upload could not recover from unauthorized response. Identifier: {Identifier}. ServerId: {ServerId}. {ItemEstimatedMarketValueSummary}", identifier, itemEstimatedMarketValueUpload.ServerId, GetItemEstimatedMarketValueUploadSummary(itemEstimatedMarketValueUpload));
                            return ReportStatus(UploadStatus.Failed);
                        }

                        response = await SendAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            await LogHttpFailure(
                                "item estimated market values",
                                requestUri,
                                identifier,
                                response,
                                "AFM item estimated market values upload unauthorized after retry.",
                                itemEstimatedMarketValueSummary: GetItemEstimatedMarketValueUploadSummary(itemEstimatedMarketValueUpload),
                                serverId: itemEstimatedMarketValueUpload.ServerId);

                            return ReportStatus(UploadStatus.Failed);
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await LogHttpFailure(
                            "item estimated market values",
                            requestUri,
                            identifier,
                            response,
                            "HTTP error while uploading item estimated market values to AFM.",
                            itemEstimatedMarketValueSummary: GetItemEstimatedMarketValueUploadSummary(itemEstimatedMarketValueUpload),
                            serverId: itemEstimatedMarketValueUpload.ServerId);

                        return ReportStatus(UploadStatus.Failed);
                    }

                    foreach (var item in itemEstimatedMarketValueUpload.Items)
                    {
                        uploadedItemEstimatedMarketValues.Add(new ItemEstimatedMarketValueUploadFingerprint(
                            itemEstimatedMarketValueUpload.ServerId,
                            item.ItemUniqueName,
                            item.Quality,
                            item.Day,
                            item.Emv));
                    }

                    Log.Information("Successfully sent {ItemsCount} item estimated market values to AFM EMV endpoint. Identifier: {Identifier}. ServerId: {ServerId}.", itemEstimatedMarketValueUpload.Items.Count, identifier, itemEstimatedMarketValueUpload.ServerId);
                    return ReportStatus(UploadStatus.Success);
                }
                finally
                {
                    response?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogAfmException(
                    ex,
                    "item estimated market values",
                    requestUri,
                    identifier,
                    itemEstimatedMarketValueSummary: GetItemEstimatedMarketValueUploadSummary(itemEstimatedMarketValueUpload),
                    serverId: itemEstimatedMarketValueUpload.ServerId);
                return ReportStatus(UploadStatus.Failed);
            }
        }

        private async Task Upload(PlayerCount playerCount)
        {
            var requestUri = httpClient.BaseAddress is null ? null : new Uri(httpClient.BaseAddress, PlayerCountPath);

            try
            {
                if (requestUri is null)
                {
                    Log.Error("Cannot upload player count because AFM base address is not initialized. {PlayerCountSummary}", GetPlayerCountSummary(playerCount));
                    return;
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                async Task<HttpResponseMessage> SendAsync()
                {
                    return await httpClient.PostAsJsonAsync(requestUri, playerCount, serializerOptions);
                }

                HttpResponseMessage? response = null;

                try
                {
                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LogHttpFailure(
                            "player count",
                            requestUri,
                            Guid.Empty,
                            response,
                            "AFM player count upload returned unauthorized. Attempting token refresh.",
                            playerCountSummary: GetPlayerCountSummary(playerCount),
                            serverId: playerCount.Server?.Id);

                        response.Dispose();
                        response = null;

                        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                        if (!recovered)
                        {
                            Log.Error("AFM player count upload could not recover from unauthorized response. {PlayerCountSummary}", GetPlayerCountSummary(playerCount));
                            return;
                        }

                        response = await SendAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            await LogHttpFailure(
                                "player count",
                                requestUri,
                                Guid.Empty,
                                response,
                                "AFM player count upload unauthorized after retry.",
                                playerCountSummary: GetPlayerCountSummary(playerCount),
                                serverId: playerCount.Server?.Id);

                            return;
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await LogHttpFailure(
                            "player count",
                            requestUri,
                            Guid.Empty,
                            response,
                            "HTTP error while uploading player count to AFM.",
                            playerCountSummary: GetPlayerCountSummary(playerCount),
                            serverId: playerCount.Server?.Id);
                        return;
                    }

                    Log.Debug("Successfully sent player count to {RequestUri}. {PlayerCountSummary}", requestUri, GetPlayerCountSummary(playerCount));
                }
                finally
                {
                    response?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogAfmException(
                    ex,
                    "player count",
                    requestUri,
                    Guid.Empty,
                    playerCountSummary: GetPlayerCountSummary(playerCount),
                    serverId: playerCount.Server?.Id);
            }
        }

        private async Task<UploadStatus> Upload(AchievementUpload achievementUpload)
        {
            var identifier = Guid.NewGuid();
            var requestUri = httpClient.BaseAddress is null ? null : new Uri(httpClient.BaseAddress, AchievementsPath);

            UploadStatus ReportStatus(UploadStatus status)
            {
                OnAchievementsUpload?.Invoke(this, new AchievementsUploadEventArgs(achievementUpload, status, UploadScope.Private, identifier));
                return status;
            }

            try
            {
                var hasValidToken = await _authService.EnsureValidTokenAsync();
                if (!hasValidToken)
                {
                    Log.Error("Cannot upload achievements without a valid Firebase session. Identifier: {Identifier}. ServerId: {ServerId}. Character: {CharacterName}. AchievementsCount: {AchievementsCount}.", identifier, achievementUpload.ServerId, achievementUpload.CharacterName, achievementUpload.Achievements.Count);
                    return ReportStatus(UploadStatus.Failed);
                }

                var firebaseUserId = _authService.FirebaseUserId;
                if (firebaseUserId is null)
                {
                    Log.Error("Cannot upload achievements without a Firebase user ID. Identifier: {Identifier}. ServerId: {ServerId}. Character: {CharacterName}. AchievementsCount: {AchievementsCount}.", identifier, achievementUpload.ServerId, achievementUpload.CharacterName, achievementUpload.Achievements.Count);
                    return ReportStatus(UploadStatus.Failed);
                }

                if (requestUri is null)
                {
                    Log.Error("Cannot upload achievements because AFM base address is not initialized. Identifier: {Identifier}. ServerId: {ServerId}. Character: {CharacterName}. AchievementsCount: {AchievementsCount}.", identifier, achievementUpload.ServerId, achievementUpload.CharacterName, achievementUpload.Achievements.Count);
                    return ReportStatus(UploadStatus.Failed);
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                async Task<HttpResponseMessage> SendAsync()
                {
                    return await httpClient.PostAsJsonAsync(requestUri, achievementUpload, serializerOptions);
                }

                HttpResponseMessage? response = null;

                try
                {
                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LogHttpFailure(
                            "achievements",
                            requestUri,
                            identifier,
                            response,
                            "AFM achievements upload returned unauthorized. Attempting token refresh.",
                            characterName: achievementUpload.CharacterName,
                            achievementsCount: achievementUpload.Achievements.Count,
                            serverId: achievementUpload.ServerId);

                        response.Dispose();
                        response = null;

                        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                        if (!recovered)
                        {
                            Log.Error("AFM achievements upload could not recover from unauthorized response. Identifier: {Identifier}. ServerId: {ServerId}. Character: {CharacterName}. AchievementsCount: {AchievementsCount}.", identifier, achievementUpload.ServerId, achievementUpload.CharacterName, achievementUpload.Achievements.Count);
                            return ReportStatus(UploadStatus.Failed);
                        }

                        response = await SendAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            await LogHttpFailure(
                                "achievements",
                                requestUri,
                                identifier,
                                response,
                                "AFM achievements upload unauthorized after retry.",
                                characterName: achievementUpload.CharacterName,
                                achievementsCount: achievementUpload.Achievements.Count,
                                serverId: achievementUpload.ServerId);

                            return ReportStatus(UploadStatus.Failed);
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await LogHttpFailure(
                            "achievements",
                            requestUri,
                            identifier,
                            response,
                            "HTTP error while uploading achievements to AFM.",
                            characterName: achievementUpload.CharacterName,
                            achievementsCount: achievementUpload.Achievements.Count,
                            serverId: achievementUpload.ServerId);

                        return ReportStatus(UploadStatus.Failed);
                    }

                    Log.Information("Successfully sent {AchievementsCount} achievements for character {CharacterName} on server {ServerId} to AFM achievements endpoint. Identifier: {Identifier}.", achievementUpload.Achievements.Count, achievementUpload.CharacterName, achievementUpload.ServerId, identifier);
                    return ReportStatus(UploadStatus.Success);
                }
                finally
                {
                    response?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogAfmException(
                    ex,
                    "achievements",
                    requestUri,
                    identifier,
                    characterName: achievementUpload.CharacterName,
                    achievementsCount: achievementUpload.Achievements.Count,
                    serverId: achievementUpload.ServerId);
                return ReportStatus(UploadStatus.Failed);
            }
        }

        private async Task<UploadStatus> Upload(GlobalMultiplierUpload globalMultiplierUpload, GlobalMultiplierUploadFingerprint fingerprint)
        {
            var identifier = Guid.NewGuid();
            var requestUri = httpClient.BaseAddress is null ? null : new Uri(httpClient.BaseAddress, GlobalMultiplierPath);

            UploadStatus ReportStatus(UploadStatus status)
            {
                OnGlobalMultiplierUpload?.Invoke(this, new GlobalMultiplierUploadEventArgs(globalMultiplierUpload, status, UploadScope.Private, identifier));
                return status;
            }

            try
            {
                var hasValidToken = await _authService.EnsureValidTokenAsync();
                if (!hasValidToken)
                {
                    Log.Error("Cannot upload global multiplier without a valid Firebase session. Identifier: {Identifier}. ServerId: {ServerId}. GlobalMultiplier: {GlobalMultiplier}.", identifier, globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
                    return ReportStatus(UploadStatus.Failed);
                }

                var firebaseUserId = _authService.FirebaseUserId;
                if (firebaseUserId is null)
                {
                    Log.Error("Cannot upload global multiplier without a Firebase user ID. Identifier: {Identifier}. ServerId: {ServerId}. GlobalMultiplier: {GlobalMultiplier}.", identifier, globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
                    return ReportStatus(UploadStatus.Failed);
                }

                if (requestUri is null)
                {
                    Log.Error("Cannot upload global multiplier because AFM base address is not initialized. Identifier: {Identifier}. ServerId: {ServerId}. GlobalMultiplier: {GlobalMultiplier}.", identifier, globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
                    return ReportStatus(UploadStatus.Failed);
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                async Task<HttpResponseMessage> SendAsync()
                {
                    return await httpClient.PostAsJsonAsync(requestUri, globalMultiplierUpload, serializerOptions);
                }

                HttpResponseMessage? response = null;

                try
                {
                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        await LogHttpFailure(
                            "global multiplier",
                            requestUri,
                            identifier,
                            response,
                            "AFM global multiplier upload returned unauthorized. Attempting token refresh.",
                            serverId: globalMultiplierUpload.ServerId,
                            globalMultiplier: globalMultiplierUpload.GlobalMultiplier);

                        response.Dispose();
                        response = null;

                        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                        if (!recovered)
                        {
                            Log.Error("AFM global multiplier upload could not recover from unauthorized response. Identifier: {Identifier}. ServerId: {ServerId}. GlobalMultiplier: {GlobalMultiplier}.", identifier, globalMultiplierUpload.ServerId, globalMultiplierUpload.GlobalMultiplier);
                            return ReportStatus(UploadStatus.Failed);
                        }

                        response = await SendAsync();

                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            await LogHttpFailure(
                                "global multiplier",
                                requestUri,
                                identifier,
                                response,
                                "AFM global multiplier upload unauthorized after retry.",
                                serverId: globalMultiplierUpload.ServerId,
                                globalMultiplier: globalMultiplierUpload.GlobalMultiplier);

                            return ReportStatus(UploadStatus.Failed);
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        await LogHttpFailure(
                            "global multiplier",
                            requestUri,
                            identifier,
                            response,
                            "HTTP error while uploading global multiplier to AFM.",
                            serverId: globalMultiplierUpload.ServerId,
                            globalMultiplier: globalMultiplierUpload.GlobalMultiplier);

                        return ReportStatus(UploadStatus.Failed);
                    }

                    uploadedGlobalMultipliers.Add(fingerprint);

                    Log.Information(
                        "Successfully sent global multiplier {GlobalMultiplier} for server {ServerId} to AFM global multiplier endpoint. Identifier: {Identifier}.",
                        globalMultiplierUpload.GlobalMultiplier,
                        globalMultiplierUpload.ServerId,
                        identifier);
                    return ReportStatus(UploadStatus.Success);
                }
                finally
                {
                    response?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogAfmException(
                    ex,
                    "global multiplier",
                    requestUri,
                    identifier,
                    serverId: globalMultiplierUpload.ServerId,
                    globalMultiplier: globalMultiplierUpload.GlobalMultiplier);
                return ReportStatus(UploadStatus.Failed);
            }
        }

        private static async Task<string> SafeReadResponseBodyAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return $"<failed to read response body: {ex.Message}>";
            }
        }

        private async Task LogHttpFailure(
            string uploadType,
            Uri requestUri,
            Guid identifier,
            HttpResponseMessage response,
            string message,
            string? marketUploadSummary = null,
            string? playerCountSummary = null,
            string? characterName = null,
            int? achievementsCount = null,
            int? serverId = null,
            double? globalMultiplier = null,
            string? itemEstimatedMarketValueSummary = null)
        {
            var responseBody = await SafeReadResponseBodyAsync(response);
            Log.Error(
                "{Message} UploadType: {UploadType}. RequestUri: {RequestUri}. Identifier: {Identifier}. StatusCode: {StatusCode}. ResponseBody: {ResponseBody}. ServerId: {ServerId}. CharacterName: {CharacterName}. AchievementsCount: {AchievementsCount}. GlobalMultiplier: {GlobalMultiplier}. MarketUploadSummary: {MarketUploadSummary}. PlayerCountSummary: {PlayerCountSummary}. ItemEstimatedMarketValueSummary: {ItemEstimatedMarketValueSummary}",
                message,
                uploadType,
                requestUri,
                identifier,
                response.StatusCode,
                responseBody,
                serverId,
                characterName,
                achievementsCount,
                globalMultiplier,
                marketUploadSummary,
                playerCountSummary,
                itemEstimatedMarketValueSummary);
        }

        private void LogAfmException(
            Exception ex,
            string uploadType,
            Uri? requestUri,
            Guid identifier,
            string? marketUploadSummary = null,
            string? playerCountSummary = null,
            string? characterName = null,
            int? achievementsCount = null,
            int? serverId = null,
            double? globalMultiplier = null,
            string? itemEstimatedMarketValueSummary = null)
        {
            Log.Error(
                ex,
                "Exception while uploading to AFM. UploadType: {UploadType}. RequestUri: {RequestUri}. Identifier: {Identifier}. ServerId: {ServerId}. CharacterName: {CharacterName}. AchievementsCount: {AchievementsCount}. GlobalMultiplier: {GlobalMultiplier}. MarketUploadSummary: {MarketUploadSummary}. PlayerCountSummary: {PlayerCountSummary}. ItemEstimatedMarketValueSummary: {ItemEstimatedMarketValueSummary}",
                uploadType,
                requestUri,
                identifier,
                serverId,
                characterName,
                achievementsCount,
                globalMultiplier,
                marketUploadSummary,
                playerCountSummary,
                itemEstimatedMarketValueSummary);
        }

        private static string GetMarketUploadSummary(MarketUpload marketUpload)
        {
            var offers = marketUpload.Orders.Count(x => x.AuctionType == AuctionType.offer);
            var requests = marketUpload.Orders.Count(x => x.AuctionType == AuctionType.request);
            var locations = string.Join(",", marketUpload.Orders.Select(x => x.Location.MarketLocation?.FriendlyName ?? "Unknown").Distinct());
            return $"Offers={offers}; Requests={requests}; Locations={locations}";
        }

        private static string GetPlayerCountSummary(PlayerCount playerCount)
        {
            return $"Server={playerCount.Server?.Name ?? "Unknown"}; ServerId={playerCount.Server?.Id}; Location={playerCount.Location?.FriendlyName ?? "Unknown"}; DateTime={playerCount.DateTime:O}; NonFlaggedCount={playerCount.NonFlaggedCount}; FlaggedCount={playerCount.FlaggedCount}; IsBz={playerCount.IsBz}";
        }

        private static string GetItemEstimatedMarketValueUploadSummary(ItemEstimatedMarketValueUpload upload)
        {
            return $"Items={upload.Items.Count}; Day={upload.Items.FirstOrDefault()?.Day}; Qualities={string.Join(",", upload.Items.Select(x => x.Quality).Distinct())}";
        }

        public void Dispose()
        {
            disposed = true;
            itemEstimatedMarketValueUploadTimer?.Dispose();
            httpClient.Dispose();
        }

        private readonly record struct ItemEstimatedMarketValueUploadKey(
            int ServerId,
            string ItemUniqueName,
            int Quality,
            DateOnly Day);

        private readonly record struct GlobalMultiplierUploadFingerprint(
            int ServerId,
            double GlobalMultiplier);

        private readonly record struct ItemEstimatedMarketValueUploadFingerprint(
            int ServerId,
            string ItemUniqueName,
            int Quality,
            DateOnly Day,
            long Emv);
    }
}
