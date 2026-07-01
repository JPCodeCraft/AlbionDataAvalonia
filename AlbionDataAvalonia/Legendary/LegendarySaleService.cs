using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Legendary;

public sealed class LegendarySaleService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SettingsManager settingsManager;
    private readonly AuthService authService;
    private readonly LegendaryDefinitionsService definitions;
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public LegendarySaleService(
        SettingsManager settingsManager,
        AuthService authService,
        LegendaryDefinitionsService definitions)
    {
        this.settingsManager = settingsManager;
        this.authService = authService;
        this.definitions = definitions;
    }

    public async Task<IReadOnlyList<LegendarySaleListing>> GetListingsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return [];
        }

        try
        {
            using var response = await SendWithUnauthorizedRecoveryAsync(
                () => httpClient.GetAsync(new Uri(GetBackendBaseUri(), "legendary-sales/mine"), cancellationToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            return await response.Content.ReadFromJsonAsync<List<LegendarySaleListing>>(JsonOptions, cancellationToken)
                ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load legendary sale listings");
            return [];
        }
    }

    public async Task<LegendarySaleOperationResult> CreateSellOrderAsync(
        LegendaryItem item,
        string priceSilver,
        string inGameName,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return LegendarySaleOperationResult.Failed("Sign in to AFM before listing an awakened item.");
        }
        if (item.SoulId is not { } soulId
            || soulId == Guid.Empty
            || item.Era is null
            || item.PvPFameGained is null
            || item.AttunementSpent is null)
        {
            return LegendarySaleOperationResult.Failed("This item is missing awakened soul metadata.");
        }

        var legendaryRating = definitions.CalculateLegendaryRating(
            item.ItemUniqueName,
            item.Traits.Select(trait => trait.Value));
        if (legendaryRating is null)
        {
            return LegendarySaleOperationResult.Failed("This item's Legendary Rating could not be calculated.");
        }

        var requestId = Guid.NewGuid().ToString();
        var itemNameFallback = string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemUniqueName : item.ItemName;
        var request = new LegendarySaleRequest(
            requestId,
            item.AlbionServerId,
            soulId.ToString(),
            item.SoulName,
            item.Era.Value,
            legendaryRating.Value,
            item.PvPFameGained.Value.ToString(CultureInfo.InvariantCulture),
            item.AttunementSpent.Value.ToString(CultureInfo.InvariantCulture),
            item.ItemUniqueName,
            ItemNameFormatter.FormatUsName(
                item.ItemUniqueName,
                definitions.FindItemUsName(item.ItemUniqueName, itemNameFallback)),
            inGameName,
            item.Quality,
            item.CrafterName,
            item.AttunedToPlayerName,
            (item.Attunement ?? 0).ToString(CultureInfo.InvariantCulture),
            item.Strain ?? 0,
            item.Traits.ConvertAll(trait =>
            {
                var displayValue = definitions.CalculateTraitValue(
                    item.ItemUniqueName,
                    item.Quality,
                    trait.TraitId,
                    trait.Value,
                    CultureInfo.GetCultureInfo("en-US"));
                return new LegendarySaleTraitRequest(
                    trait.TraitId,
                    definitions.FindTrait(trait.TraitId)?.UsName,
                    trait.Value,
                    displayValue.FormattedText);
            }),
            priceSilver);

        try
        {
            using var response = await SendWithUnauthorizedRecoveryAsync(
                () => httpClient.PostAsJsonAsync(
                    new Uri(GetBackendBaseUri(), "legendary-sales"),
                    request,
                    JsonOptions,
                    cancellationToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return LegendarySaleOperationResult.Failed(
                    response.StatusCode == HttpStatusCode.Unauthorized
                        ? "Sign in to AFM before listing an awakened item."
                        : await ReadErrorAsync(response, cancellationToken));
            }

            var listing = await response.Content.ReadFromJsonAsync<LegendarySaleListing>(JsonOptions, cancellationToken);
            if (listing is null)
            {
                return LegendarySaleOperationResult.Failed("The backend returned an empty sale listing response.");
            }
            var sellOrder = listing.SellOrders.FirstOrDefault(order => order.RequestId == requestId);
            return LegendarySaleOperationResult.Succeeded(listing, sellOrder);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to list legendary soul {SoulId} for sale", item.SoulId);
            return LegendarySaleOperationResult.Failed("Failed to contact the AFM awakened sale service.");
        }
    }

    public async Task<LegendarySaleOperationResult> SetSoldAsync(
        int serverId,
        Guid soulId,
        bool sold,
        CancellationToken cancellationToken = default)
    {
        return await UpdateListingAsync(serverId, soulId, new { sold }, cancellationToken);
    }

    public async Task<LegendarySaleOperationResult> CancelAsync(
        int serverId,
        Guid soulId,
        CancellationToken cancellationToken = default)
    {
        return await UpdateListingAsync(serverId, soulId, new { canceled = true }, cancellationToken);
    }

    private async Task<LegendarySaleOperationResult> UpdateListingAsync(
        int serverId,
        Guid soulId,
        object state,
        CancellationToken cancellationToken)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return LegendarySaleOperationResult.Failed("Sign in to AFM before updating an awakened listing.");
        }

        try
        {
            using var response = await SendWithUnauthorizedRecoveryAsync(
                () => SendListingUpdateRequestAsync(serverId, soulId, state, cancellationToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return LegendarySaleOperationResult.Failed(
                    response.StatusCode == HttpStatusCode.Unauthorized
                        ? "Sign in to AFM before updating an awakened listing."
                        : await ReadErrorAsync(response, cancellationToken));
            }

            var listing = await response.Content.ReadFromJsonAsync<LegendarySaleListing>(JsonOptions, cancellationToken);
            return listing is null
                ? LegendarySaleOperationResult.Failed("The backend returned an empty sale listing response.")
                : LegendarySaleOperationResult.Updated(listing);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update legendary soul {SoulId} sale state", soulId);
            return LegendarySaleOperationResult.Failed("Failed to contact the AFM awakened sale service.");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private Task<HttpResponseMessage> SendListingUpdateRequestAsync(
        int serverId,
        Guid soulId,
        object state,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri(GetBackendBaseUri(), $"legendary-sales/{serverId}/{soulId}"))
        {
            Content = JsonContent.Create(state, options: JsonOptions)
        };
        return httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<bool> EnsureValidAuthAsync(CancellationToken cancellationToken)
    {
        if (!await authService.EnsureValidTokenAsync(cancellationToken: cancellationToken)
            || authService.CurrentFirebaseUser is null)
        {
            return false;
        }
        ApplyAuthHeader();
        return true;
    }

    private void ApplyAuthHeader()
    {
        httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(authService.CurrentFirebaseUser?.IdToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", authService.CurrentFirebaseUser.IdToken);
    }

    private async Task<HttpResponseMessage> SendWithUnauthorizedRecoveryAsync(
        Func<Task<HttpResponseMessage>> sendAsync,
        CancellationToken cancellationToken)
    {
        var response = await sendAsync();
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }
        response.Dispose();
        if (!await authService.TryRecoverFromUnauthorizedAsync(cancellationToken))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
        ApplyAuthHeader();
        return await sendAsync();
    }

    private Uri GetBackendBaseUri()
    {
        return settingsManager.AppSettings.GetAfmBackendApiBaseUri();
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<SaleErrorResponse>(JsonOptions, cancellationToken);
            if (result is not null && !string.IsNullOrWhiteSpace(result.Error))
            {
                return result.Error;
            }
        }
        catch
        {
        }
        return $"Awakened sale request failed ({(int)response.StatusCode}).";
    }

    private sealed record LegendarySaleRequest(
        string RequestId,
        int ServerId,
        string SoulId,
        string? SoulName,
        int Era,
        long LegendaryRating,
        string PvpFameGained,
        string AttunementSpent,
        string ItemUniqueName,
        string ItemDisplayName,
        string InGameName,
        int Quality,
        string? CrafterName,
        string? AttunedToPlayerName,
        string Attunement,
        double Strain,
        List<LegendarySaleTraitRequest> Traits,
        string PriceSilver);

    private sealed record LegendarySaleTraitRequest(string Id, string? Name, double Value, string DisplayValue);

    private sealed class SaleErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }
}

public sealed record LegendarySaleListing(
    int ServerId,
    string SoulId,
    bool Sold,
    DateTimeOffset? SoldAt,
    bool Canceled,
    string LatestPriceSilver,
    string LatestInGameName,
    string? LatestDiscordUsername,
    DateTimeOffset LastListedAt,
    IReadOnlyList<LegendarySellOrder> SellOrders,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LegendarySellOrder(
    string RequestId,
    string PriceSilver,
    string InGameName,
    string? DiscordUsername,
    DateTimeOffset CreatedAt,
    LegendaryDiscordDelivery Discord);

public sealed record LegendaryDiscordDelivery(
    string Status,
    string? MessageId,
    string? MessageUrl,
    string? ChannelId,
    DateTimeOffset? PostedAt,
    int? RetryAfterSeconds);

public sealed record LegendarySaleOperationResult(
    bool Success,
    string Message,
    LegendarySaleListing? Listing,
    string? MessageUrl,
    int? RetryAfterSeconds)
{
    public static LegendarySaleOperationResult Succeeded(
        LegendarySaleListing listing,
        LegendarySellOrder? sellOrder)
    {
        var message = sellOrder?.Discord.Status switch
        {
            "posted" => "Awakened item listed on AFM and posted to Discord.",
            "not_linked" => "Awakened item listed on AFM. Link Discord at https://albionfreemarket.com/account to also announce it there.",
            "not_member" => "Awakened item listed on AFM. Join the AFM Discord server to also announce it there.",
            "rate_limited" => "Awakened item listed on AFM. The Discord announcement was rate-limited.",
            "failed" => "Awakened item listed on AFM, but the Discord announcement failed.",
            _ => "Awakened item listed on AFM. Discord delivery is currently unavailable."
        };
        return new(true, message, listing, sellOrder?.Discord.MessageUrl, sellOrder?.Discord.RetryAfterSeconds);
    }

    public static LegendarySaleOperationResult Updated(LegendarySaleListing listing) =>
        new(
            true,
            listing.Sold
                ? "Awakened item marked as sold."
                : listing.Canceled
                    ? "Awakened sale listing canceled."
                    : "Awakened item marked as available.",
            listing,
            null,
            null);

    public static LegendarySaleOperationResult Failed(string message) =>
        new(false, message, null, null, null);
}
