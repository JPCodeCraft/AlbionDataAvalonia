using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Farming;

public sealed class FarmingUploadService : IDisposable
{
    private const int MaxBatchBytes = 80 * 1024;
    private const int MaxObservationBytes = MaxBatchBytes - 512;
    private static readonly TimeSpan UploadInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SettingsManager _settingsManager;
    private readonly AuthService _authService;
    private readonly PlayerState _playerState;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly Channel<QueueMessage> _queue = Channel.CreateUnbounded<QueueMessage>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly Dictionary<string, AccountOutbox> _outboxes = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sessionLock = new();
    private readonly Task _worker;
    private readonly Timer _timer;
    private CancellationTokenSource _sessionCancellation = new();
    private string? _sessionAccountId;
    private int _disposed;

    public FarmingUploadService(SettingsManager settingsManager, AuthService authService, PlayerState playerState)
    {
        _settingsManager = settingsManager;
        _authService = authService;
        _playerState = playerState;
        _sessionAccountId = _authService.FirebaseUserId;
        _authService.FirebaseUserChanged += OnFirebaseUserChanged;
        _settingsManager.UserSettings.PropertyChanged += OnSettingsChanged;
        _worker = Task.Run(ProcessQueueAsync);
        _timer = new Timer(_ => QueueUpload(), null, UploadInterval, UploadInterval);
        QueueUpload();
    }

    public bool EnqueueIsland(string accountId, FarmingIslandObservation value)
    {
        return Enqueue(accountId, value);
    }

    public bool EnqueueObject(string accountId, FarmingObjectObservation value)
    {
        return Enqueue(accountId, value);
    }

    public bool EnqueuePickup(string accountId, FarmingPickup value)
    {
        return Enqueue(accountId, value);
    }

    private bool Enqueue<T>(string accountId, T value) where T : FarmingContext
    {
        // The caller captures the account at observation time; never adopt a later login.
        if (Volatile.Read(ref _disposed) != 0 || !_settingsManager.UserSettings.AfmIslandTrackerEnabled
            || string.IsNullOrWhiteSpace(accountId)
            || !string.Equals(accountId, _authService.FirebaseUserId, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            // Reject malformed numbers and records that can never fit in an upload batch.
            if (JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions).Length > MaxObservationBytes)
            {
                Log.Warning("Skipping a farming observation exceeding the upload size limit");
                return false;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            Log.Warning(ex, "Skipping a farming observation that cannot be serialized");
            return false;
        }

        return _settingsManager.UserSettings.AfmIslandTrackerEnabled
            && string.Equals(accountId, _authService.FirebaseUserId, StringComparison.Ordinal)
            && _queue.Writer.TryWrite(new QueueMessage(accountId, value, false));
    }

    private void QueueUpload()
    {
        if (Volatile.Read(ref _disposed) == 0 && _settingsManager.UserSettings.AfmIslandTrackerEnabled)
        {
            _queue.Writer.TryWrite(new QueueMessage(_authService.FirebaseUserId, null, true));
        }
    }

    private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
    {
        UpdateUploadSession(user?.LocalId);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.AfmIslandTrackerEnabled)) return;
        UpdateUploadSession(_authService.FirebaseUserId, reset: true);
        // Preserve data captured before tracking was disabled without starting an upload.
        if (!_settingsManager.UserSettings.AfmIslandTrackerEnabled)
        {
            _queue.Writer.TryWrite(new QueueMessage(null, null, true));
        }
    }

    private void UpdateUploadSession(string? accountId, bool reset = false)
    {
        lock (_sessionLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (reset || !string.Equals(_sessionAccountId, accountId, StringComparison.Ordinal))
            {
                _sessionCancellation.Cancel();
                _sessionCancellation.Dispose();
                _sessionCancellation = new CancellationTokenSource();
                _sessionAccountId = accountId;
            }
        }

        QueueUpload();
    }

    private async Task ProcessQueueAsync()
    {
        while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var uploadRequested = false;
            var processed = 0;
            while (processed++ < 4096 && _queue.Reader.TryRead(out var message))
            {
                uploadRequested |= message.Upload;
                if (string.IsNullOrWhiteSpace(message.AccountId))
                {
                    continue;
                }

                var outbox = await LoadOutboxAsync(message.AccountId).ConfigureAwait(false);
                if (message.Value != null)
                {
                    Apply(outbox, message.Value);
                }
            }

            // Snapshot bursts coalesce in memory until the scheduled flush. Rewriting the
            // full durable outbox for each packet becomes expensive while uploads are offline.
            if (uploadRequested)
            {
                foreach (var outbox in _outboxes.Values.Where(value => value.Dirty))
                {
                    await PersistAsync(outbox).ConfigureAwait(false);
                }
            }

            if (uploadRequested && Volatile.Read(ref _disposed) == 0
                && _settingsManager.UserSettings.AfmIslandTrackerEnabled
                && _authService.FirebaseUserId is { Length: > 0 } accountId
                && _outboxes.TryGetValue(accountId, out var current))
            {
                await UploadBatchAsync(current).ConfigureAwait(false);
            }
        }

        foreach (var outbox in _outboxes.Values.Where(value => value.Dirty))
        {
            await PersistAsync(outbox).ConfigureAwait(false);
        }
    }

    private async Task<AccountOutbox> LoadOutboxAsync(string accountId)
    {
        if (_outboxes.TryGetValue(accountId, out var existing))
        {
            return existing;
        }

        var outbox = new AccountOutbox { AccountId = accountId };
        var path = GetOutboxPath(accountId);
        if (File.Exists(path))
        {
            try
            {
                AccountOutbox? stored;
                await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
                {
                    stored = await JsonSerializer.DeserializeAsync<AccountOutbox>(stream, SerializerOptions).ConfigureAwait(false);
                }
                if (stored == null || !string.Equals(stored.AccountId, accountId, StringComparison.Ordinal)
                    || stored.Islands is null || stored.Objects is null || stored.Pickups is null
                    || stored.Islands.Values.Any(value => value is null)
                    || stored.Objects.Values.Any(value => value is null)
                    || stored.Pickups.Values.Any(value => value is null))
                {
                    throw new InvalidDataException("Farming outbox has an invalid account or collection.");
                }

                outbox = stored;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                // Preserve unreadable pending loot, but let new observations continue to sync.
                var backupPath = path + $".unreadable-{Guid.NewGuid():N}";
                try
                {
                    File.Move(path, backupPath);
                    Log.Error(ex, "Preserved unreadable farming outbox at {BackupPath}; starting a new outbox", backupPath);
                }
                catch (Exception backupException)
                {
                    outbox.StorageUnavailable = true;
                    Log.Error(backupException, "Cannot preserve unreadable farming outbox; new observations remain in memory");
                }
            }
            catch (Exception ex)
            {
                // Keep the original file intact rather than overwriting unreadable pending loot.
                outbox.StorageUnavailable = true;
                Log.Error(ex, "Cannot read farming outbox; new observations remain in memory until the client restarts");
            }
        }

        _outboxes.Add(accountId, outbox);
        return outbox;
    }

    private static void Apply(AccountOutbox outbox, FarmingContext value)
    {
        var key = GetPendingKey(value);
        switch (value)
        {
            case FarmingIslandObservation island:
                if (!outbox.Islands.TryGetValue(key, out var oldIsland) || island.ObservedAt >= oldIsland.ObservedAt)
                {
                    outbox.Islands[key] = island with
                    {
                        OwnerName = island.OwnerName ?? oldIsland?.OwnerName,
                        IslandName = island.IslandName ?? oldIsland?.IslandName,
                        HomeCluster = island.HomeCluster ?? oldIsland?.HomeCluster,
                        LayoutId = island.LayoutId ?? oldIsland?.LayoutId
                    };
                    outbox.Dirty = true;
                }
                break;
            case FarmingObjectObservation farmObject:
                if (!outbox.Objects.TryGetValue(key, out var oldObject) || farmObject.ObservedAt >= oldObject.ObservedAt)
                {
                    var preservePendingState = !farmObject.Removed && farmObject.State is null
                        && oldObject is { Removed: false, State: not null };
                    outbox.Objects[key] = farmObject with
                    {
                        State = preservePendingState ? oldObject!.State : farmObject.State,
                        // A new metadata packet must not make an old farming snapshot look fresh.
                        ObservedAt = preservePendingState ? oldObject!.ObservedAt : farmObject.ObservedAt
                    };
                    outbox.Dirty = true;
                }
                break;
            case FarmingPickup pickup:
                outbox.Dirty |= outbox.Pickups.TryAdd(key, pickup);
                break;
        }
    }

    private static string GetPendingKey(FarmingContext value)
    {
        if (value is FarmingPickup pickup) return pickup.EventId;
        var key = $"{value.ServerId}:{value.CharacterId}:{value.IslandId}";
        return value is FarmingObjectObservation farmObject ? $"{key}:{farmObject.ObjectId}" : key;
    }

    private static async Task PersistAsync(AccountOutbox outbox)
    {
        if (outbox.StorageUnavailable)
        {
            return;
        }

        try
        {
            var path = GetOutboxPath(outbox.AccountId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, outbox, SerializerOptions).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, overwrite: true);
            outbox.Dirty = false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not persist farming observations; retaining them for retry");
        }
    }

    private async Task UploadBatchAsync(AccountOutbox outbox)
    {
        if (!_settingsManager.UserSettings.AfmIslandTrackerEnabled
            || outbox.Dirty || outbox.StorageUnavailable || DateTime.UtcNow < outbox.NextAttemptAtUtc)
        {
            return;
        }

        var batch = BuildBatch(outbox);
        if (batch.Islands.Count + batch.Objects.Count + batch.Pickups.Count == 0)
        {
            return;
        }

        using var cancellation = CreateUploadCancellation(outbox.AccountId);
        if (cancellation == null)
        {
            return;
        }

        var identifier = Guid.NewGuid();
        UploadStatus? status = null;
        try
        {
            if (!await _authService.EnsureValidTokenAsync(cancellationToken: cancellation.Token).ConfigureAwait(false))
            {
                if (!cancellation.IsCancellationRequested) ScheduleRetry(outbox);
                return;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(batch, SerializerOptions);
            status = UploadStatus.Failed;
            using var response = await SendAsync(outbox.AccountId, payload, cancellation.Token).ConfigureAwait(false);
            if (response == null)
            {
                if (!cancellation.IsCancellationRequested) ScheduleRetry(outbox);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                ScheduleRetry(outbox);
                Log.Warning("Farming upload returned HTTP {StatusCode}; retaining observations for retry", (int)response.StatusCode);
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<UploadResponse>(SerializerOptions, cancellation.Token).ConfigureAwait(false);
            if (result?.Accepted != true)
            {
                ScheduleRetry(outbox);
                Log.Warning("Farming upload was not acknowledged; retaining observations for retry");
                return;
            }

            status = UploadStatus.Success;
            RemoveAccepted(outbox.Islands, batch.Islands);
            RemoveAccepted(outbox.Objects, batch.Objects);
            RemoveAccepted(outbox.Pickups, batch.Pickups);
            outbox.Dirty = true;
            outbox.Failures = 0;
            outbox.NextAttemptAtUtc = DateTime.UtcNow + UploadInterval;
            await PersistAsync(outbox).ConfigureAwait(false);
            Log.Debug("Uploaded farming observations: {IslandCount} islands, {ObjectCount} objects and {PickupCount} pickups",
                batch.Islands.Count, batch.Objects.Count, batch.Pickups.Count);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Disabling tracking, logout and account changes leave pending data intact.
            status = null;
        }
        catch (Exception ex)
        {
            ScheduleRetry(outbox);
            Log.Warning(ex, "Farming upload failed; retaining observations for retry");
        }
        finally
        {
            if (status.HasValue && !cancellation.IsCancellationRequested)
            {
                _playerState.RecordIslandUpload(status.Value, identifier);
            }
        }
    }

    private CancellationTokenSource? CreateUploadCancellation(string accountId)
    {
        lock (_sessionLock)
        {
            return Volatile.Read(ref _disposed) == 0
                && _settingsManager.UserSettings.AfmIslandTrackerEnabled
                && string.Equals(accountId, _sessionAccountId, StringComparison.Ordinal)
                && string.Equals(accountId, _authService.FirebaseUserId, StringComparison.Ordinal)
                ? CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, _sessionCancellation.Token)
                : null;
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(string accountId, byte[] payload, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var user = _authService.CurrentFirebaseUser;
            if (!_settingsManager.UserSettings.AfmIslandTrackerEnabled
                || user == null || !string.Equals(user.LocalId, accountId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(user.IdToken))
            {
                return null;
            }

            var uri = new Uri(_settingsManager.AppSettings.GetAfmBackendApiBaseUri(), "dataclient/farming");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new ByteArrayContent(payload)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.IdToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_settingsManager.UserSettings.AfmIslandTrackerEnabled
                || !string.Equals(accountId, _authService.FirebaseUserId, StringComparison.Ordinal))
            {
                return null;
            }

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) || attempt != 0)
            {
                return response;
            }

            response.Dispose();
            if (!string.Equals(accountId, _authService.FirebaseUserId, StringComparison.Ordinal)
                || !await _authService.TryRecoverFromUnauthorizedAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    private static UploadBatch BuildBatch(AccountOutbox outbox)
    {
        var batch = new UploadBatch();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(batch, SerializerOptions).Length;

        // Pickups take priority so a busy stream of snapshots cannot starve the loot history.
        AddToBatch(outbox.Pickups.Values, batch.Pickups, 100, ref bytes);
        AddToBatch(outbox.Islands.Values, batch.Islands, 100, ref bytes);
        AddToBatch(outbox.Objects.Values, batch.Objects, 250, ref bytes);
        return batch;
    }

    private static void AddToBatch<T>(IEnumerable<T> pending, List<T> destination, int maxCount, ref int bytes)
    {
        foreach (var value in pending)
        {
            if (destination.Count >= maxCount)
            {
                break;
            }

            var size = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions).Length + 1;
            // Old outbox files may contain records current capture would reject.
            // Do not let an unuploadable record block valid records behind it.
            if (size - 1 > MaxObservationBytes) continue;
            if (bytes + size > MaxBatchBytes)
            {
                // Leave the remaining records for the next batch instead of serializing
                // the entire offline backlog after the byte budget has been exhausted.
                break;
            }

            destination.Add(value);
            bytes += size;
        }
    }

    private static void RemoveAccepted<T>(Dictionary<string, T> pending, List<T> accepted) where T : FarmingContext
    {
        foreach (var value in accepted)
        {
            var key = GetPendingKey(value);
            if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, value))
                pending.Remove(key);
        }
    }

    private static void ScheduleRetry(AccountOutbox outbox)
    {
        outbox.Failures = Math.Min(outbox.Failures + 1, 6);
        outbox.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, outbox.Failures)));
    }

    private static string GetOutboxPath(string accountId)
    {
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId))).ToLowerInvariant();
        return Path.Combine(AppData.DataDirectoryPath, "farming-outbox", fileName + ".json");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
        _authService.FirebaseUserChanged -= OnFirebaseUserChanged;
        _settingsManager.UserSettings.PropertyChanged -= OnSettingsChanged;
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
        // Finish persisting already accepted observations, without attempting another upload.
        _worker.GetAwaiter().GetResult();
        _httpClient.Dispose();
        _shutdown.Dispose();
        lock (_sessionLock)
        {
            _sessionCancellation.Dispose();
        }
    }

    private sealed record QueueMessage(string? AccountId, FarmingContext? Value, bool Upload);

    private sealed class AccountOutbox
    {
        public string AccountId { get; set; } = string.Empty;
        public Dictionary<string, FarmingIslandObservation> Islands { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, FarmingObjectObservation> Objects { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, FarmingPickup> Pickups { get; set; } = new(StringComparer.Ordinal);
        [JsonIgnore] public bool Dirty { get; set; }
        [JsonIgnore] public bool StorageUnavailable { get; set; }
        [JsonIgnore] public DateTime NextAttemptAtUtc { get; set; }
        [JsonIgnore] public int Failures { get; set; }
    }

    private sealed class UploadBatch
    {
        public int SchemaVersion { get; } = 1;
        public List<FarmingIslandObservation> Islands { get; } = [];
        public List<FarmingObjectObservation> Objects { get; } = [];
        public List<FarmingPickup> Pickups { get; } = [];
    }

    private sealed class UploadResponse
    {
        public bool Accepted { get; set; }
    }
}
