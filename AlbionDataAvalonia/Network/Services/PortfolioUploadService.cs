using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public sealed class PortfolioUploadService : IDisposable
{
    public const int MaxPortfolioImportPostCount = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SettingsManager _settingsManager;
    private readonly AuthService _authService;
    private readonly HttpClient _httpClient = new();

    public PortfolioUploadService(SettingsManager settingsManager, AuthService authService)
    {
        _settingsManager = settingsManager;
        _authService = authService;
    }

    public Task<bool> CanUploadAsync(CancellationToken cancellationToken = default)
    {
        return EnsureValidAuthAsync(cancellationToken);
    }

    public static bool IsPostLimitExceeded(int postCount)
    {
        return postCount > MaxPortfolioImportPostCount;
    }

    public static string CreatePostLimitMessage(int postCount)
    {
        return $"Selection would update {postCount:N0} Portfolio positions. Select at most {MaxPortfolioImportPostCount:N0} item/server/quality groups.";
    }

    public static int EstimatePortfolioPostCount(IEnumerable<PortfolioTradePostEstimate> trades)
    {
        var keys = new HashSet<PositionKey>();
        foreach (var trade in trades)
        {
            if (trade.LocationIndex < 0 || string.IsNullOrWhiteSpace(trade.ItemId) || trade.Amount <= 0)
            {
                continue;
            }

            var key = CreatePositionKey(trade.ItemId, trade.AlbionServerId, trade.QualityIndex);
            if (key != null)
            {
                keys.Add(key.Value);
            }
        }

        return keys.Count;
    }

    public async Task<PortfolioUploadedTradeIdsResult> GetUploadedTradeIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            const string message = "Sign in to AFM before adding trades to Portfolio.";
            Log.Warning("Portfolio uploaded trade id refresh skipped because the user is not signed in");
            return PortfolioUploadedTradeIdsResult.Failed(message);
        }

        try
        {
            var positions = await LoadPortfolioPositionsAsync(cancellationToken);
            var uploadedTradeIds = CreateUploadedTradeIdSet(positions);
            Log.Debug(
                "Loaded {UploadedTradeIdCount} uploaded Portfolio data client trade ids from {PositionCount} positions",
                uploadedTradeIds.Count,
                positions.Count);
            return PortfolioUploadedTradeIdsResult.Succeeded(uploadedTradeIds);
        }
        catch (PortfolioUploadException ex)
        {
            Log.Warning("Portfolio uploaded trade id refresh failed: {Reason}", ex.Message);
            return PortfolioUploadedTradeIdsResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Portfolio uploaded trade id refresh failed");
            return PortfolioUploadedTradeIdsResult.Failed("Failed to load portfolio positions.");
        }
    }

    public async Task<PortfolioImportResult> ImportTradesAsync(
        IReadOnlyCollection<PortfolioTradeImportRequest> requests,
        bool allowReupload,
        CancellationToken cancellationToken = default)
    {
        var result = new PortfolioImportResult { RequestedCount = requests.Count };
        if (requests.Count == 0)
        {
            Log.Debug("Portfolio import skipped because no trades were provided");
            return result;
        }

        var groupedRequests = requests.GroupBy(CreatePositionKey).ToList();
        var postCount = groupedRequests.Count(group => group.Key != null);
        if (IsPostLimitExceeded(postCount))
        {
            var message = CreatePostLimitMessage(postCount);
            Log.Warning(
                "Portfolio import rejected before upload because it would require {PostCount} position saves. MaxPostCount={MaxPostCount} TradeCount={TradeCount}",
                postCount,
                MaxPortfolioImportPostCount,
                requests.Count);
            foreach (var request in requests)
            {
                result.FailedTradeIds.Add(request.TradeId);
            }

            result.Errors.Add(message);
            return result;
        }

        Log.Debug(
            "Starting Portfolio import for {TradeCount} trades. PostCount={PostCount} AllowReupload={AllowReupload}",
            requests.Count,
            postCount,
            allowReupload);

        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            const string message = "Sign in to AFM before adding trades to Portfolio.";
            Log.Warning("Portfolio import failed before upload: {Reason}. TradeCount={TradeCount}", message, requests.Count);
            FailAll(result, requests, message);
            return result;
        }

        List<PortfolioPositionDto> positions;
        try
        {
            positions = await LoadPortfolioPositionsAsync(cancellationToken);

            Log.Debug("Loaded {PositionCount} Portfolio positions before importing {TradeCount} trades", positions.Count, requests.Count);
        }
        catch (PortfolioUploadException ex)
        {
            Log.Warning("Portfolio import failed while loading positions: {Reason}", ex.Message);
            FailAll(result, requests, ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Portfolio import failed while loading positions");
            FailAll(result, requests, "Failed to load portfolio positions.");
            return result;
        }

        var positionLookup = CreatePositionLookup(positions);
        var uploadedTradeIds = CreateUploadedTradeIdSet(positions);

        foreach (var group in groupedRequests)
        {
            var key = group.Key;
            if (key == null)
            {
                foreach (var request in group)
                {
                    result.FailedTradeIds.Add(request.TradeId);
                    Log.Warning(
                        "Portfolio import failed for trade {TradeId}: unsupported AlbionServerId {AlbionServerId}. Item={ItemId} Quality={QualityIndex}",
                        request.TradeId,
                        request.AlbionServerId,
                        request.ItemId,
                        request.QualityIndex);
                }
                result.Errors.Add("Some trades could not be mapped to an AFM server.");
                continue;
            }

            positionLookup.TryGetValue(key.Value, out var position);

            if (position == null)
            {
                Log.Debug(
                    "Portfolio import will create position for item {ItemId} on {Server} with quality {QualityIndex}",
                    key.Value.UniqueName,
                    key.Value.Server,
                    key.Value.QualityIndex);
            }
            else
            {
                Log.Debug(
                    "Portfolio import found existing position {PositionId} for item {ItemId} on {Server} with quality {QualityIndex}",
                    position.Id,
                    key.Value.UniqueName,
                    key.Value.Server,
                    key.Value.QualityIndex);
            }

            var transactionsToAppend = new List<(PortfolioTradeImportRequest Request, PortfolioTransactionDto Transaction, bool AlreadyPresent)>();
            foreach (var request in group)
            {
                var alreadyPresent = uploadedTradeIds.Contains(request.TradeId);
                if (!allowReupload && alreadyPresent)
                {
                    const string reason = "A transaction with this AFM Data Client trade id already exists in Portfolio. Confirm reupload to add it again.";
                    result.SkippedTradeIds.Add(request.TradeId);
                    AddWarning(result, reason);
                    Log.Warning(
                        "Portfolio import skipped trade {TradeId}: {Reason} PositionId={PositionId} Item={ItemId} Server={Server} Quality={QualityIndex}",
                        request.TradeId,
                        reason,
                        position?.Id,
                        request.ItemId,
                        key.Value.Server,
                        request.QualityIndex);
                    continue;
                }

                transactionsToAppend.Add((request, CreateTransaction(request), alreadyPresent));
                Log.Debug(
                    "Portfolio import queued trade {TradeId} for item {ItemId} on {Server} with quality {QualityIndex}. Operation={Operation} Type={TradeType} Amount={Amount} UnitSilver={UnitSilver} AllowReupload={AllowReupload}",
                    request.TradeId,
                    request.ItemId,
                    key.Value.Server,
                    request.QualityIndex,
                    request.TradeOperation,
                    request.TradeType,
                    request.Amount,
                    request.UnitSilver,
                    allowReupload);
            }

            if (transactionsToAppend.Count == 0)
            {
                Log.Debug(
                    "Portfolio import had no transactions to save for item {ItemId} on {Server} with quality {QualityIndex}",
                    key.Value.UniqueName,
                    key.Value.Server,
                    key.Value.QualityIndex);
                continue;
            }

            position ??= new PortfolioPositionDto
            {
                UniqueName = key.Value.UniqueName,
                Server = key.Value.Server,
                QualityIndex = key.Value.QualityIndex
            };

            position.Transactions.AddRange(transactionsToAppend.Select(x => x.Transaction));

            try
            {
                Log.Debug(
                    "Saving Portfolio position for item {ItemId} on {Server} with quality {QualityIndex}. AppendedTransactions={TransactionCount}",
                    key.Value.UniqueName,
                    key.Value.Server,
                    key.Value.QualityIndex,
                    transactionsToAppend.Count);

                var savedPosition = await SendWithUnauthorizedRecoveryAsync(
                    () => _httpClient.PostAsJsonAsync(GetPortfolioPositionsUri(), position, SerializerOptions, cancellationToken),
                    response => response.Content.ReadFromJsonAsync<PortfolioPositionDto>(SerializerOptions, cancellationToken));

                if (savedPosition != null)
                {
                    positions.RemoveAll(candidate => !string.IsNullOrEmpty(savedPosition.Id) && candidate.Id == savedPosition.Id);
                    positions.Add(savedPosition);
                    positionLookup[key.Value] = savedPosition;

                    Log.Debug(
                        "Saved Portfolio position {PositionId} for item {ItemId} on {Server} with quality {QualityIndex}. TransactionCount={TransactionCount}",
                        savedPosition.Id,
                        key.Value.UniqueName,
                        key.Value.Server,
                        key.Value.QualityIndex,
                        savedPosition.Transactions.Count);
                }

                foreach (var item in transactionsToAppend)
                {
                    result.ImportedTradeIds.Add(item.Request.TradeId);
                    if (item.AlreadyPresent)
                    {
                        result.ReuploadedTradeIds.Add(item.Request.TradeId);
                    }
                    uploadedTradeIds.Add(item.Request.TradeId);

                    Log.Information(
                        "Portfolio import succeeded for trade {TradeId}. Item={ItemId} Server={Server} Quality={QualityIndex} Reupload={Reupload}",
                        item.Request.TradeId,
                        item.Request.ItemId,
                        key.Value.Server,
                        item.Request.QualityIndex,
                        item.AlreadyPresent);
                }
            }
            catch (PortfolioUploadException ex)
            {
                foreach (var item in transactionsToAppend)
                {
                    result.FailedTradeIds.Add(item.Request.TradeId);
                    Log.Warning(
                        "Portfolio import failed for trade {TradeId}: {Reason}. Item={ItemId} Server={Server} Quality={QualityIndex}",
                        item.Request.TradeId,
                        ex.Message,
                        item.Request.ItemId,
                        key.Value.Server,
                        item.Request.QualityIndex);
                }
                result.Errors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Portfolio import failed while saving position for {UniqueName} {Server} Q{QualityIndex}", key.Value.UniqueName, key.Value.Server, key.Value.QualityIndex);
                foreach (var item in transactionsToAppend)
                {
                    result.FailedTradeIds.Add(item.Request.TradeId);
                    Log.Warning(
                        "Portfolio import failed for trade {TradeId}: failed to save position. Item={ItemId} Server={Server} Quality={QualityIndex}",
                        item.Request.TradeId,
                        item.Request.ItemId,
                        key.Value.Server,
                        item.Request.QualityIndex);
                }
                result.Errors.Add($"Failed to save {key.Value.UniqueName} to Portfolio.");
            }
        }

        Log.Debug(
            "Finished Portfolio import. Requested={RequestedCount} Imported={ImportedCount} Reuploaded={ReuploadedCount} Skipped={SkippedCount} Failed={FailedCount}",
            result.RequestedCount,
            result.ImportedCount,
            result.ReuploadedCount,
            result.SkippedCount,
            result.FailedCount);

        return result;
    }

    private async Task<bool> EnsureValidAuthAsync(CancellationToken cancellationToken)
    {
        var hasValidToken = await _authService.EnsureValidTokenAsync(cancellationToken: cancellationToken);
        if (!hasValidToken || _authService.CurrentFirebaseUser is null)
        {
            return false;
        }

        ApplyAuthHeaders();
        return true;
    }

    private void ApplyAuthHeaders()
    {
        var user = _authService.CurrentFirebaseUser;
        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(user?.IdToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", user.IdToken);

        _httpClient.DefaultRequestHeaders.Remove("X-User-Id");
        if (!string.IsNullOrWhiteSpace(user?.LocalId))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", user.LocalId);
        }
    }

    private async Task<List<PortfolioPositionDto>> LoadPortfolioPositionsAsync(CancellationToken cancellationToken)
    {
        return await SendWithUnauthorizedRecoveryAsync(
            () => _httpClient.GetAsync(GetPortfolioPositionsUri(), cancellationToken),
            response => response.Content.ReadFromJsonAsync<List<PortfolioPositionDto>>(SerializerOptions, cancellationToken)) ?? new List<PortfolioPositionDto>();
    }

    private static Dictionary<PositionKey, PortfolioPositionDto> CreatePositionLookup(IEnumerable<PortfolioPositionDto> positions)
    {
        var lookup = new Dictionary<PositionKey, PortfolioPositionDto>();
        foreach (var position in positions)
        {
            var key = CreatePositionKey(position);
            if (key == null || lookup.ContainsKey(key.Value))
            {
                continue;
            }

            lookup.Add(key.Value, position);
        }

        return lookup;
    }

    private static HashSet<Guid> CreateUploadedTradeIdSet(IEnumerable<PortfolioPositionDto> positions)
    {
        var tradeIds = new HashSet<Guid>();
        foreach (var transaction in positions.SelectMany(position => position.Transactions))
        {
            if (Guid.TryParse(transaction.DataClientTradeId, out var tradeId))
            {
                tradeIds.Add(tradeId);
            }
        }

        return tradeIds;
    }

    private async Task<T?> SendWithUnauthorizedRecoveryAsync<T>(
        Func<Task<HttpResponseMessage>> sendAsync,
        Func<HttpResponseMessage, Task<T?>> readAsync)
    {
        using var response = await SendWithUnauthorizedRecoveryAsync(sendAsync);
        if (!response.IsSuccessStatusCode)
        {
            throw new PortfolioUploadException(await CreateFailureMessageAsync(response));
        }

        return await readAsync(response);
    }

    private async Task<HttpResponseMessage> SendWithUnauthorizedRecoveryAsync(Func<Task<HttpResponseMessage>> sendAsync)
    {
        var response = await sendAsync();
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        Log.Warning("Portfolio request returned unauthorized; refreshing token and retrying once");

        var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
        if (!recovered)
        {
            Log.Warning("Portfolio request unauthorized recovery failed");
            throw new PortfolioUploadException("Your AFM session expired. Sign in again before adding trades to Portfolio.");
        }

        Log.Debug("Portfolio request unauthorized recovery succeeded");
        ApplyAuthHeaders();
        return await sendAsync();
    }

    private Uri GetPortfolioPositionsUri()
    {
        return new Uri(GetBackendApiBaseUri(), "portfolio/positions");
    }

    private Uri GetBackendApiBaseUri()
    {
        var rawBase = _settingsManager.AppSettings.AfmBackendApiBase;
        if (string.IsNullOrWhiteSpace(rawBase))
        {
            rawBase = _settingsManager.AppSettings.AfmAuthApiUrl;
            if (rawBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                rawBase = rawBase[..^"/api".Length];
            }
        }

        if (string.IsNullOrWhiteSpace(rawBase))
        {
            rawBase = "https://api.albionfreemarket.com/be";
        }

        return new Uri(rawBase.TrimEnd('/') + "/");
    }

    private static PositionKey? CreatePositionKey(PortfolioTradeImportRequest request)
    {
        return CreatePositionKey(request.ItemId, request.AlbionServerId, request.QualityIndex);
    }

    private static PositionKey? CreatePositionKey(string itemId, int? albionServerId, int qualityIndex)
    {
        var server = albionServerId switch
        {
            1 => "west",
            2 => "east",
            3 => "europe",
            _ => null
        };

        return server is null
            ? null
            : new PositionKey(itemId, NormalizeServer(server), qualityIndex);
    }

    private static PositionKey? CreatePositionKey(PortfolioPositionDto position)
    {
        if (string.IsNullOrWhiteSpace(position.UniqueName) || string.IsNullOrWhiteSpace(position.Server))
        {
            return null;
        }

        return new PositionKey(position.UniqueName, NormalizeServer(position.Server), position.QualityIndex);
    }

    private static string NormalizeServer(string server)
    {
        return server.Trim().ToLowerInvariant();
    }

    private static PortfolioTransactionDto CreateTransaction(PortfolioTradeImportRequest request)
    {
        var transaction = new PortfolioTransactionDto
        {
            Operation = request.TradeOperation == TradeOperation.Buy ? "Buy" : "Sell",
            Amount = request.Amount,
            UnitPrice = request.UnitSilver,
            Timestamp = ToIsoTimestamp(request.DateTime),
            LocationIndex = request.LocationIndex.ToString(),
            DataClientTradeId = request.TradeId.ToString("D")
        };

        if (request.TradeOperation == TradeOperation.Buy)
        {
            transaction.BuyFromType = request.TradeType == TradeType.Order ? "Buy Order" : "Sell Order";
        }
        else
        {
            transaction.SellToType = request.TradeType == TradeType.Order ? "Sell Order" : "Buy Order";
            transaction.HasPremium = true;
        }

        return transaction;
    }

    private static string ToIsoTimestamp(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return dateTime.ToUniversalTime().ToString("O");
    }

    private static async Task<string> CreateFailureMessageAsync(HttpResponseMessage response)
    {
        var body = await SafeReadResponseBodyAsync(response);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return "Portfolio requires AFM Masterpiece entitlement.";
        }

        return $"Portfolio upload failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}";
    }

    private static async Task<string> SafeReadResponseBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void FailAll(
        PortfolioImportResult result,
        IEnumerable<PortfolioTradeImportRequest> requests,
        string message)
    {
        foreach (var request in requests)
        {
            result.FailedTradeIds.Add(request.TradeId);
            Log.Warning("Portfolio import failed for trade {TradeId}: {Reason}", request.TradeId, message);
        }

        result.Errors.Add(message);
    }

    private static void AddWarning(PortfolioImportResult result, string message)
    {
        if (!result.Warnings.Contains(message, StringComparer.Ordinal))
        {
            result.Warnings.Add(message);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private readonly record struct PositionKey(string UniqueName, string Server, int QualityIndex);

    private sealed class PortfolioUploadException : Exception
    {
        public PortfolioUploadException(string message)
            : base(message)
        {
        }
    }
}
