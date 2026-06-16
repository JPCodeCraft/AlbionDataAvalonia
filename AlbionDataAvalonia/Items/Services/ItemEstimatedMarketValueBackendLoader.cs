using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services;

public sealed class ItemEstimatedMarketValueBackendLoader : IDisposable
{
    private const string NewestEstimatedMarketValuesPath = "itemEstimatedMarketValues/newestEMV";
    private const int FlushThreshold = 20;
    private const int MaxUniqueNamesPerRequest = 100;
    private const int MaxRetries = 3;
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly int[] UnknownQualityLookupQualities = [1, 2, 3, 4];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SettingsManager settingsManager;
    private readonly AuthService authService;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly HttpClient httpClient = new();
    private readonly ConcurrentDictionary<PendingLookupKey, PendingLookup> pendingLookups = new();
    private readonly ConcurrentDictionary<NegativeLookupKey, DateTime> negativeLookups = new();
    private readonly object timerLock = new();
    private readonly object headersLock = new();
    private Timer? flushTimer;
    private int flushInProgress;
    private bool disposed;

    public ItemEstimatedMarketValueBackendLoader(
        SettingsManager settingsManager,
        AuthService authService,
        ItemEstimatedMarketValueService itemEstimatedMarketValues)
    {
        this.settingsManager = settingsManager;
        this.authService = authService;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.authService.FirebaseUserChanged += OnFirebaseUserChanged;
    }

    public void Initialize()
    {
        httpClient.BaseAddress = new Uri(settingsManager.AppSettings.AfmTopItemsApiBase);
        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        var version = AlbionDataAvalonia.ClientUpdater.GetVersion() ?? "unknown";
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"afmDataClient-v.{version}");
        httpClient.DefaultRequestHeaders.Referrer = new Uri("https://github.com/JPCodeCraft/AlbionDataAvalonia");
        UpdateAuthHeader(authService.CurrentFirebaseUser);
    }

    public void QueueMissingEstimatedMarketValue(int serverId, int itemId, string itemUniqueName, int? quality)
    {
        if (serverId <= 0 || itemId <= 0 || string.IsNullOrWhiteSpace(itemUniqueName))
        {
            return;
        }

        var normalizedUniqueName = itemUniqueName.Trim();
        if (string.Equals(normalizedUniqueName, "Unknown Item", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedUniqueName, "Unset", StringComparison.OrdinalIgnoreCase)
            || normalizedUniqueName.StartsWith("Unknown Item", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var qualities = quality is >= 1 and <= 5
            ? [quality.Value]
            : UnknownQualityLookupQualities;

        var queued = false;
        foreach (var wantedQuality in qualities)
        {
            var negativeKey = new NegativeLookupKey(serverId, normalizedUniqueName, wantedQuality);
            if (IsNegativeCached(negativeKey))
            {
                continue;
            }

            var key = new PendingLookupKey(serverId, itemId, normalizedUniqueName, wantedQuality);
            queued |= pendingLookups.TryAdd(key, new PendingLookup(key, 0));
        }

        if (!queued)
        {
            return;
        }

        if (authService.CurrentFirebaseUser is null)
        {
            Log.Debug(
                "Queued backend EMV lookup but no Firebase session is available. ServerId: {ServerId}. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. PendingCount: {PendingCount}.",
                serverId,
                normalizedUniqueName,
                quality,
                pendingLookups.Count);
            return;
        }

        ScheduleFlush(pendingLookups.Count >= FlushThreshold ? TimeSpan.Zero : FlushDelay);
    }

    private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
    {
        UpdateAuthHeader(user);
        if (user is not null && !pendingLookups.IsEmpty)
        {
            ScheduleFlush(TimeSpan.Zero);
        }
    }

    private void UpdateAuthHeader(FirebaseAuthResponse? user)
    {
        lock (headersLock)
        {
            try
            {
                if (user is not null
                    && !string.IsNullOrWhiteSpace(user.IdToken)
                    && !string.IsNullOrWhiteSpace(user.LocalId))
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.IdToken);
                    httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                    if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", user.LocalId))
                    {
                        Log.Warning("Failed to set X-User-Id header for backend EMV lookup due to validation.");
                    }
                    return;
                }

                httpClient.DefaultRequestHeaders.Authorization = null;
                httpClient.DefaultRequestHeaders.Remove("X-User-Id");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while updating backend EMV lookup auth header; clearing headers as a fallback.");
                httpClient.DefaultRequestHeaders.Authorization = null;
                httpClient.DefaultRequestHeaders.Remove("X-User-Id");
            }
        }
    }

    private void ScheduleFlush(TimeSpan delay)
    {
        lock (timerLock)
        {
            if (disposed)
            {
                return;
            }

            flushTimer ??= new Timer(_ => _ = FlushAsync());
            flushTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushAsync()
    {
        if (Interlocked.Exchange(ref flushInProgress, 1) == 1)
        {
            ScheduleFlush(FlushDelay);
            return;
        }

        try
        {
            if (pendingLookups.IsEmpty)
            {
                return;
            }

            if (authService.CurrentFirebaseUser is null)
            {
                Log.Debug("Skipping backend EMV lookup flush because no Firebase session exists. PendingCount: {PendingCount}.", pendingLookups.Count);
                return;
            }

            var hasValidToken = await authService.EnsureValidTokenAsync();
            if (!hasValidToken)
            {
                Log.Debug("Skipping backend EMV lookup flush because no valid Firebase session exists. PendingCount: {PendingCount}.", pendingLookups.Count);
                return;
            }

            var snapshot = pendingLookups.ToArray();
            if (snapshot.Length == 0)
            {
                return;
            }

            foreach (var serverGroup in snapshot.GroupBy(pair => pair.Key.ServerId))
            {
                var serverLookups = new List<PendingLookup>();
                foreach (var pair in serverGroup)
                {
                    if (pendingLookups.TryRemove(pair.Key, out var lookup))
                    {
                        serverLookups.Add(lookup);
                    }
                }

                if (serverLookups.Count == 0)
                {
                    continue;
                }

                foreach (var chunk in ChunkByUniqueNames(serverLookups, MaxUniqueNamesPerRequest))
                {
                    await LoadChunkAsync(serverGroup.Key, chunk);
                }
            }

            if (!pendingLookups.IsEmpty && authService.CurrentFirebaseUser is not null)
            {
                ScheduleFlush(FlushDelay);
            }
        }
        finally
        {
            Interlocked.Exchange(ref flushInProgress, 0);
        }
    }

    private async Task LoadChunkAsync(int serverId, List<PendingLookup> lookups)
    {
        var uniqueNames = lookups
            .Select(lookup => lookup.Key.ItemUniqueName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var qualities = lookups
            .Select(lookup => lookup.Key.Quality)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        var requestUri = CreateNewestEstimatedMarketValuesUri(serverId, uniqueNames, qualities);
        try
        {
            async Task<HttpResponseMessage> SendAsync()
            {
                return await httpClient.GetAsync(requestUri);
            }

            using var response = await SendWithUnauthorizedRecoveryAsync(SendAsync);
            if (response is null)
            {
                RequeueLookups(lookups);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await SafeReadResponseBodyAsync(response);
                Log.Error(
                    "Backend EMV lookup failed. RequestUri: {RequestUri}. StatusCode: {StatusCode}. ResponseBody: {ResponseBody}. ItemsCount: {ItemsCount}. Qualities: {Qualities}.",
                    requestUri,
                    response.StatusCode,
                    responseBody,
                    uniqueNames.Length,
                    string.Join(",", qualities));

                if (response.StatusCode is HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    RequeueLookups(lookups);
                }
                return;
            }

            var values = await response.Content.ReadFromJsonAsync<List<ItemEstimatedMarketValueResponse>>(SerializerOptions)
                ?? new List<ItemEstimatedMarketValueResponse>();
            ApplyLoadedValues(serverId, lookups, values);
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Exception while loading backend EMV values. RequestUri: {RequestUri}. ItemsCount: {ItemsCount}. Qualities: {Qualities}.",
                requestUri,
                uniqueNames.Length,
                string.Join(",", qualities));
            RequeueLookups(lookups);
        }
    }

    private Uri CreateNewestEstimatedMarketValuesUri(int serverId, IReadOnlyList<string> uniqueNames, IReadOnlyList<int> qualities)
    {
        var query = $"server={serverId}"
            + $"&uniqueNames={Uri.EscapeDataString(string.Join(",", uniqueNames))}"
            + $"&qualities={Uri.EscapeDataString(string.Join(",", qualities))}";
        return new Uri(httpClient.BaseAddress!, $"{NewestEstimatedMarketValuesPath}?{query}");
    }

    private async Task<HttpResponseMessage?> SendWithUnauthorizedRecoveryAsync(Func<Task<HttpResponseMessage>> sendAsync)
    {
        var response = await sendAsync();
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var recovered = await authService.TryRecoverFromUnauthorizedAsync();
        if (!recovered)
        {
            Log.Error("Backend EMV lookup could not recover from unauthorized response.");
            return null;
        }

        UpdateAuthHeader(authService.CurrentFirebaseUser);
        return await sendAsync();
    }

    private void ApplyLoadedValues(int serverId, List<PendingLookup> lookups, List<ItemEstimatedMarketValueResponse> values)
    {
        var requestedKeys = lookups
            .Select(lookup => new NegativeLookupKey(serverId, lookup.Key.ItemUniqueName, lookup.Key.Quality))
            .ToHashSet();
        var itemIdsByUniqueName = lookups
            .GroupBy(lookup => lookup.Key.ItemUniqueName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(lookup => lookup.Key.ItemId).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var returnedKeys = new HashSet<NegativeLookupKey>();
        foreach (var value in values.Where(value => value.Emv > 0 && value.Quality is >= 1 and <= 5))
        {
            returnedKeys.Add(new NegativeLookupKey(serverId, value.ItemUniqueName, value.Quality));
            if (!itemIdsByUniqueName.TryGetValue(value.ItemUniqueName, out var itemIds))
            {
                continue;
            }

            foreach (var itemId in itemIds)
            {
                itemEstimatedMarketValues.Update(serverId, itemId, value.Quality, value.Emv);
            }
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var missingKey in requestedKeys.Except(returnedKeys))
        {
            negativeLookups[missingKey] = nowUtc;
        }

        Log.Debug(
            "Backend EMV lookup complete. ServerId: {ServerId}. RequestedEntries: {RequestedEntries}. ReturnedEntries: {ReturnedEntries}.",
            serverId,
            lookups.Count,
            values.Count);
    }

    private void RequeueLookups(List<PendingLookup> lookups)
    {
        var requeued = 0;
        foreach (var lookup in lookups)
        {
            if (lookup.Attempts >= MaxRetries)
            {
                continue;
            }

            var nextLookup = lookup with { Attempts = lookup.Attempts + 1 };
            pendingLookups[lookup.Key] = nextLookup;
            requeued++;
        }

        if (requeued > 0)
        {
            ScheduleFlush(RetryDelay);
        }
    }

    private bool IsNegativeCached(NegativeLookupKey key)
    {
        if (!negativeLookups.TryGetValue(key, out var cachedAtUtc))
        {
            return false;
        }

        if (DateTime.UtcNow - cachedAtUtc <= NegativeCacheDuration)
        {
            return true;
        }

        negativeLookups.TryRemove(key, out _);
        return false;
    }

    private static IEnumerable<List<PendingLookup>> ChunkByUniqueNames(List<PendingLookup> lookups, int maxUniqueNames)
    {
        var chunks = new List<List<PendingLookup>>();
        var current = new List<PendingLookup>();
        var currentUniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lookup in lookups.OrderBy(lookup => lookup.Key.ItemUniqueName, StringComparer.OrdinalIgnoreCase))
        {
            if (!currentUniqueNames.Contains(lookup.Key.ItemUniqueName)
                && currentUniqueNames.Count >= maxUniqueNames)
            {
                chunks.Add(current);
                current = new List<PendingLookup>();
                currentUniqueNames.Clear();
            }

            current.Add(lookup);
            currentUniqueNames.Add(lookup.Key.ItemUniqueName);
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        return chunks;
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

    public void Dispose()
    {
        disposed = true;
        authService.FirebaseUserChanged -= OnFirebaseUserChanged;
        flushTimer?.Dispose();
        httpClient.Dispose();
    }

    private readonly record struct PendingLookupKey(
        int ServerId,
        int ItemId,
        string ItemUniqueName,
        int Quality);

    private readonly record struct NegativeLookupKey(
        int ServerId,
        string ItemUniqueName,
        int Quality);

    private readonly record struct PendingLookup(PendingLookupKey Key, int Attempts);

    private sealed record ItemEstimatedMarketValueResponse(
        string ItemUniqueName,
        int Server,
        int Quality,
        DateOnly Day,
        long Emv);
}
