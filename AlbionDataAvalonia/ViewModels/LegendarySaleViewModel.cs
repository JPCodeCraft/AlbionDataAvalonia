using AlbionDataAvalonia.Legendary;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public sealed record LegendarySaleInput(string PriceSilver, string InGameName);

/// <summary>
/// Lists an awakened item for sale, updates its price or relists it, then reports where the listing appeared:
/// on the AFM website, on Discord, or why Discord was skipped.
/// </summary>
public sealed partial class LegendarySaleViewModel : ViewModelBase
{
    private const string DiscordSetupHelp = "For future automatic Discord posts, make sure Discord is linked on your AFM account and that you have joined the AFM Discord server.";

    private readonly Func<LegendarySaleInput, Task<LegendarySaleOperationResult>> submitAsync;
    private readonly SaleAction action;
    private readonly string submitText;
    private string? discordMessageUrl;

    public LegendarySaleViewModel(
        LegendaryItemRowViewModel item,
        string defaultInGameName,
        Func<LegendarySaleInput, Task<LegendarySaleOperationResult>> submitAsync)
    {
        Item = item;
        this.submitAsync = submitAsync;
        action = !item.HasListing ? SaleAction.List : item.IsActiveListing ? SaleAction.UpdatePrice : SaleAction.Relist;
        submitText = item.SaleActionText;
        inGameName = defaultInGameName;
        priceText = item.Listing?.LatestPriceSilver ?? "";
        title = Pick("List awakened item for sale", "Update awakened listing", "Relist awakened item");
        subtitle = $"{item.ServerName} · {Pick("Listing preview", "Price update preview", "Relisting preview")}";
        websiteStatus = Pick("AFM website — Will be listed", "AFM website — Listing price will be updated", "AFM website — Item will be relisted");
    }

    public LegendaryItemRowViewModel Item { get; }
    public event Action? CloseRequested;

    [ObservableProperty] private string title;
    [ObservableProperty] private string subtitle;
    [ObservableProperty] private string deliveryTitle = "Where this listing will appear";
    [ObservableProperty] private string websiteStatus;
    [ObservableProperty] private string discordStatus = "Discord — AFM will attempt an automatic post";
    [ObservableProperty] private string discordHelp = "For the automatic Discord post, make sure Discord is linked on your AFM account and that you have joined the AFM Discord server.";
    [ObservableProperty] private bool showDiscordSetup = true;
    [ObservableProperty] private bool showAccountLink = true;
    [ObservableProperty] private bool showDiscordInvite = true;
    [ObservableProperty] private bool showOpenDiscordPost;
    [ObservableProperty] private string priceText;
    [ObservableProperty] private string inGameName;
    [ObservableProperty] private string validationText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditInputs), nameof(SubmitText))]
    private bool isPosting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditInputs), nameof(ShowSubmit), nameof(CancelText))]
    private bool isPosted;

    public bool CanEditInputs => !IsPosting && !IsPosted;
    public bool ShowSubmit => !IsPosted;
    public string SubmitText => IsPosting ? Pick("Posting…", "Updating…", "Relisting…") : submitText;
    public string CancelText => IsPosted ? "Close" : "Cancel";

    [RelayCommand]
    private async Task Submit()
    {
        if (IsPosting || IsPosted) return;

        if (NormalizePrice(PriceText) is not { } price)
        {
            ValidationText = "Enter a positive whole-silver price with at most 18 digits.";
            return;
        }

        var name = (InGameName ?? "").Trim();
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
        {
            ValidationText = "Enter an in-game character name with at most 100 characters.";
            return;
        }

        ValidationText = "";
        IsPosting = true;
        try
        {
            var result = await submitAsync(new LegendarySaleInput(price, name));
            if (result.Success) ShowPosted(result);
            else ShowFailed(result.Message);
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not post the awakened item listing.");
            ShowFailed("The listing could not be posted. Please try again.");
        }
        finally
        {
            IsPosting = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsPosting) CloseRequested?.Invoke();
    }

    [RelayCommand] private void OpenCalculator() => SystemLauncher.TryOpenOptionalUrl(Item.CalculatorUrl);
    [RelayCommand] private void OpenAccount() => SystemLauncher.TryOpenOptionalUrl(SystemLauncher.AfmAccountUrl);
    [RelayCommand] private void OpenDiscordInvite() => SystemLauncher.TryOpenOptionalUrl(SystemLauncher.AfmDiscordUrl);
    [RelayCommand] private void OpenDiscordPost() => SystemLauncher.TryOpenOptionalUrl(discordMessageUrl);

    private void ShowPosted(LegendarySaleOperationResult result)
    {
        IsPosted = true;
        Title = Pick("Awakened item listed", "Awakened listing updated", "Awakened item relisted");
        Subtitle = $"{Item.ServerName} · {Pick("Listed just now", "Updated just now", "Relisted just now")}";
        DeliveryTitle = Pick("Listing posted", "Listing updated", "Item relisted");
        WebsiteStatus = Pick("AFM website — Posted", "AFM website — Price updated", "AFM website — Relisted");
        ShowDiscordSetup = false;
        ShowOpenDiscordPost = false;
        discordMessageUrl = result.MessageUrl;
        ValidationText = "";

        switch (result.DiscordStatus)
        {
            case "posted":
                DiscordStatus = "Discord — Posted";
                DiscordHelp = "This listing is live on both the AFM website and Discord.";
                ShowOpenDiscordPost = !string.IsNullOrWhiteSpace(discordMessageUrl);
                break;
            case "not_linked":
                ShowWebsiteOnly("Discord — Not posted. Make sure Discord is linked on your AFM account.");
                break;
            case "not_member":
                ShowWebsiteOnly("Discord — Not posted. Make sure the Discord account linked to AFM has joined the AFM Discord server.");
                break;
            case "rate_limited":
                DiscordStatus = "Discord — Rate-limited; this listing is website-only for now.";
                DiscordHelp = result.RetryAfterSeconds is { } retryAfter
                    ? $"Discord can be tried again in {TimeSpan.FromSeconds(retryAfter):g}."
                    : "Discord delivery was rate-limited. The AFM website listing is still active.";
                break;
            case "failed":
                DiscordStatus = "Discord — Posting failed; this listing is website-only.";
                DiscordHelp = "The AFM website listing succeeded, but its Discord announcement failed.";
                break;
            case "pending":
                DiscordStatus = "Discord — Delivery pending";
                DiscordHelp = "The AFM website listing is live while Discord delivery is still being processed.";
                break;
            default:
                ShowWebsiteOnly("Discord — Delivery could not be confirmed; this listing is website-only.");
                break;
        }
    }

    private void ShowWebsiteOnly(string status)
    {
        DiscordStatus = status;
        DiscordHelp = DiscordSetupHelp;
        ShowDiscordSetup = true;
        ShowAccountLink = true;
        ShowDiscordInvite = true;
    }

    private void ShowFailed(string message)
    {
        var updating = action == SaleAction.UpdatePrice;
        DeliveryTitle = updating ? "Update not applied" : "Not posted";
        WebsiteStatus = updating ? "AFM website — Existing listing was not changed" : "AFM website — Not posted";
        DiscordStatus = "Discord — Not posted";
        DiscordHelp = updating
            ? "The existing listing is unchanged. Correct any problem and try again."
            : "Nothing was published. Correct any problem and try again.";
        ShowDiscordSetup = false;
        ValidationText = message;
    }

    private string Pick(string list, string updatePrice, string relist) => action switch
    {
        SaleAction.UpdatePrice => updatePrice,
        SaleAction.Relist => relist,
        _ => list
    };

    private static string? NormalizePrice(string? value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length is < 1 or > 18 || normalized.Any(character => character is < '0' or > '9')) return null;
        normalized = normalized.TrimStart('0');
        return normalized.Length == 0 ? null : normalized;
    }

    private enum SaleAction { List, UpdatePrice, Relist }
}
