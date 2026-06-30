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

public sealed class LegendaryDiscordSaleService : IDisposable
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

    public LegendaryDiscordSaleService(
        SettingsManager settingsManager,
        AuthService authService,
        LegendaryDefinitionsService definitions)
    {
        this.settingsManager = settingsManager;
        this.authService = authService;
        this.definitions = definitions;
    }

    public async Task<LegendarySaleEligibility> GetEligibilityAsync(int serverId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return new LegendarySaleEligibility(false, "not_signed_in", null, null);
        }

        try
        {
            using var response = await SendWithUnauthorizedRecoveryAsync(
                () => httpClient.GetAsync(new Uri(GetBackendBaseUri(), $"discord/legendary-sales/eligibility?serverId={serverId}"), cancellationToken),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new LegendarySaleEligibility(false, "not_signed_in", null, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                return new LegendarySaleEligibility(false, "service_unavailable", null, null);
            }
            return await response.Content.ReadFromJsonAsync<LegendarySaleEligibility>(JsonOptions, cancellationToken)
                ?? new LegendarySaleEligibility(false, "service_unavailable", null, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check Discord legendary sale eligibility");
            return new LegendarySaleEligibility(false, "service_unavailable", null, null);
        }
    }

    public async Task<IReadOnlyList<LegendarySalePostSummary>> GetPostsAsync(CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return Array.Empty<LegendarySalePostSummary>();
        }

        try
        {
            using var response = await SendWithUnauthorizedRecoveryAsync(
                () => httpClient.GetAsync(new Uri(GetBackendBaseUri(), "discord/legendary-sales"), cancellationToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<LegendarySalePostSummary>();
            }
            return await response.Content.ReadFromJsonAsync<List<LegendarySalePostSummary>>(JsonOptions, cancellationToken)
                ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load Discord legendary sale history");
            return Array.Empty<LegendarySalePostSummary>();
        }
    }

    public async Task<LegendarySalePostResult> PostAsync(
        LegendaryItem item,
        string priceSilver,
        string inGameName,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureValidAuthAsync(cancellationToken))
        {
            return LegendarySalePostResult.Failed("Sign in to AFM before posting a legendary item.");
        }
        if (item.SoulId is not { } soulId
            || soulId == Guid.Empty
            || item.Era is null
            || item.PvPFameGained is null
            || item.AttunementSpent is null)
        {
            return LegendarySalePostResult.Failed("This item is missing legendary soul metadata.");
        }
        var legendaryRating = definitions.CalculateLegendaryRating(
            item.ItemUniqueName,
            item.Traits.Select(trait => trait.Value));
        if (legendaryRating is null)
        {
            return LegendarySalePostResult.Failed("This item's legendary rating could not be calculated.");
        }

        var itemNameFallback = string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemUniqueName : item.ItemName;
        var itemDisplayName = ItemNameFormatter.FormatUsName(
            item.ItemUniqueName,
            definitions.FindItemUsName(item.ItemUniqueName, itemNameFallback));
        var request = new LegendarySalePostRequest(
            Guid.NewGuid().ToString(),
            item.AlbionServerId,
            soulId.ToString(),
            item.SoulName,
            item.Era.Value,
            legendaryRating.Value,
            item.PvPFameGained.Value.ToString(CultureInfo.InvariantCulture),
            item.AttunementSpent.Value.ToString(CultureInfo.InvariantCulture),
            item.ItemUniqueName,
            itemDisplayName,
            inGameName,
            item.Quality,
            item.CrafterName,
            item.AttunedToPlayerName,
            (item.Attunement ?? 0).ToString(),
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
                    new Uri(GetBackendBaseUri(), "discord/legendary-sales"),
                    request,
                    JsonOptions,
                    cancellationToken),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return LegendarySalePostResult.Failed("Sign in to AFM before posting a legendary item.");
                }
                var error = await ReadErrorAsync(response, cancellationToken);
                return LegendarySalePostResult.Failed(error.DisplayMessage, error.RetryAfterSeconds, error.InviteUrl);
            }
            var posted = await response.Content.ReadFromJsonAsync<LegendarySalePostResponse>(JsonOptions, cancellationToken);
            return posted is null || string.IsNullOrWhiteSpace(posted.MessageUrl)
                ? LegendarySalePostResult.Failed("The backend returned an empty Discord post response.")
                : LegendarySalePostResult.Succeeded(posted.MessageUrl, posted.CreatedAt);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to post legendary soul {SoulId} to Discord", item.SoulId);
            return LegendarySalePostResult.Failed("Failed to contact the AFM Discord sale service.");
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
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

    private static async Task<SaleErrorResponse> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<SaleErrorResponse>(JsonOptions, cancellationToken);
            if (result is not null && !string.IsNullOrWhiteSpace(result.DisplayMessage))
            {
                return result;
            }
        }
        catch
        {
        }
        return new SaleErrorResponse
        {
            Message = $"Discord sale post failed ({(int)response.StatusCode})."
        };
    }

    private sealed record LegendarySalePostRequest(
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
    private sealed record LegendarySalePostResponse(string MessageUrl, DateTimeOffset CreatedAt);

    private sealed class SaleErrorResponse
    {
        public string Message { get; set; } = string.Empty;
        public string? Error { get; set; }
        public int? RetryAfterSeconds { get; set; }
        public string? InviteUrl { get; set; }

        public string DisplayMessage => string.IsNullOrWhiteSpace(Error) ? Message : Error;
    }
}

public sealed record LegendarySaleEligibility(bool CanPost, string Reason, string? DiscordUsername, string? InviteUrl)
{
    public string Description => Reason switch
    {
        "eligible" => $"Posting as {DiscordUsername ?? "your linked Discord account"}.",
        "not_signed_in" => "Sign in to AFM to post legendary items.",
        "discord_not_linked" => "Link Discord to your AFM account at https://albionfreemarket.com/account before posting.",
        "not_guild_member" => "Join the AFM Discord server before posting.",
        "invalid_server" => "The item must have a known Albion server.",
        _ => "Discord legendary sales are temporarily unavailable."
    };
}

public sealed record LegendarySalePostSummary(int ServerId, string SoulId, DateTimeOffset CreatedAt);

public sealed record LegendarySalePostResult(
    string Message,
    string? MessageUrl,
    DateTimeOffset? PostedAt,
    int? RetryAfterSeconds,
    string? InviteUrl)
{
    public static LegendarySalePostResult Succeeded(string messageUrl, DateTimeOffset postedAt) =>
        new("Legendary item posted to AFM Discord.", messageUrl, postedAt, null, null);

    public static LegendarySalePostResult Failed(string message, int? retryAfterSeconds = null, string? inviteUrl = null) =>
        new(message, null, null, retryAfterSeconds, inviteUrl);
}
