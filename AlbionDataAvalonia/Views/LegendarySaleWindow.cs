using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class LegendarySaleWindow : Window
{
    private const string AccountUrl = "https://albionfreemarket.com/account";
    private const string DiscordInviteUrl = "https://discord.com/invite/BPmDE3zznH";

    private LegendaryItemRowViewModel? item;
    private Func<LegendarySaleInput, Task<LegendarySaleOperationResult>>? submitAsync;
    private string? discordMessageUrl;
    private string submitButtonText = "List for sale";
    private SaleAction saleAction;
    private bool isPosting;
    private bool isPosted;

    public LegendarySaleWindow()
    {
        InitializeComponent();
        Closing += Window_Closing;
    }

    private LegendarySaleWindow(
        LegendaryItemRowViewModel item,
        string defaultInGameName,
        Func<LegendarySaleInput, Task<LegendarySaleOperationResult>> submitAsync)
        : this()
    {
        this.item = item;
        this.submitAsync = submitAsync;
        DataContext = item;
        InGameNameTextBox.Text = defaultInGameName;
        PriceTextBox.Text = item.Listing?.LatestPriceSilver ?? string.Empty;
        saleAction = GetSaleAction(item);
        submitButtonText = item.SaleActionText;
        Title = saleAction switch
        {
            SaleAction.UpdatePrice => "Update awakened listing",
            SaleAction.Relist => "Relist awakened item",
            _ => "List awakened item for sale"
        };
        ListingSubtitleTextBlock.Text = $"{item.ServerName} · {GetPreviewLabel()}";
        WebsiteStatusTextBlock.Text = saleAction switch
        {
            SaleAction.UpdatePrice => "AFM website — Listing price will be updated",
            SaleAction.Relist => "AFM website — Item will be relisted",
            _ => "AFM website — Will be listed"
        };
        SubmitButton.Content = submitButtonText;
        CalculatorButton.IsVisible = item.CanViewInCalculator;
    }

    public static async Task ShowAsync(
        Window owner,
        LegendaryItemRowViewModel item,
        string defaultInGameName,
        Func<LegendarySaleInput, Task<LegendarySaleOperationResult>> submitAsync)
    {
        var window = new LegendarySaleWindow(item, defaultInGameName, submitAsync);
        await window.ShowDialog(owner);
    }

    private async void SubmitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (isPosting || isPosted || submitAsync is null)
        {
            return;
        }

        var priceSilver = NormalizePrice(PriceTextBox.Text);
        if (priceSilver is null)
        {
            ValidationTextBlock.Text = "Enter a positive whole-silver price with at most 18 digits.";
            return;
        }

        var inGameName = (InGameNameTextBox.Text ?? string.Empty).Trim();
        if (inGameName.Length is < 1 or > 100 || inGameName.Any(char.IsControl))
        {
            ValidationTextBlock.Text = "Enter an in-game character name with at most 100 characters.";
            return;
        }

        ValidationTextBlock.Text = string.Empty;
        SetPostingState(true);
        try
        {
            var result = await submitAsync(new LegendarySaleInput(priceSilver, inGameName));
            if (result.Success)
            {
                ApplySuccessfulResult(result);
            }
            else
            {
                ApplyFailedResult(result.Message);
            }
        }
        catch
        {
            ApplyFailedResult("The listing could not be posted. Please try again.");
        }
        finally
        {
            SetPostingState(false);
        }
    }

    private void ApplySuccessfulResult(LegendarySaleOperationResult result)
    {
        isPosted = true;
        Title = saleAction switch
        {
            SaleAction.UpdatePrice => "Awakened listing updated",
            SaleAction.Relist => "Awakened item relisted",
            _ => "Awakened item listed"
        };
        ListingSubtitleTextBlock.Text = $"{item?.ServerName ?? "Unknown"} · {GetCompletedLabel()}";
        DeliveryTitleTextBlock.Text = saleAction switch
        {
            SaleAction.UpdatePrice => "Listing updated",
            SaleAction.Relist => "Item relisted",
            _ => "Listing posted"
        };
        WebsiteStatusTextBlock.Text = saleAction switch
        {
            SaleAction.UpdatePrice => "AFM website — Price updated",
            SaleAction.Relist => "AFM website — Relisted",
            _ => "AFM website — Posted"
        };
        DiscordSetupButtonsPanel.IsVisible = false;
        DiscordHelpTextBlock.IsVisible = true;
        OpenDiscordPostButton.IsVisible = false;
        discordMessageUrl = result.MessageUrl;

        switch (result.DiscordStatus)
        {
            case "posted":
                DiscordStatusTextBlock.Text = "Discord — Posted";
                DiscordHelpTextBlock.Text = "This listing is live on both the AFM website and Discord.";
                OpenDiscordPostButton.IsVisible = !string.IsNullOrWhiteSpace(discordMessageUrl);
                break;
            case "not_linked":
                ShowWebsiteOnlyResult(
                    "Discord — Not posted. Make sure Discord is linked on your AFM account.",
                    showAccountLink: true,
                    showDiscordInvite: true);
                break;
            case "not_member":
                ShowWebsiteOnlyResult(
                    "Discord — Not posted. Make sure the Discord account linked to AFM has joined the AFM Discord server.",
                    showAccountLink: true,
                    showDiscordInvite: true);
                break;
            case "rate_limited":
                DiscordStatusTextBlock.Text = "Discord — Rate-limited; this listing is website-only for now.";
                DiscordHelpTextBlock.Text = result.RetryAfterSeconds is { } retryAfter
                    ? $"Discord can be tried again in {TimeSpan.FromSeconds(retryAfter):g}."
                    : "Discord delivery was rate-limited. The AFM website listing is still active.";
                break;
            case "failed":
                DiscordStatusTextBlock.Text = "Discord — Posting failed; this listing is website-only.";
                DiscordHelpTextBlock.Text = "The AFM website listing succeeded, but its Discord announcement failed.";
                break;
            case "pending":
                DiscordStatusTextBlock.Text = "Discord — Delivery pending";
                DiscordHelpTextBlock.Text = "The AFM website listing is live while Discord delivery is still being processed.";
                break;
            default:
                ShowWebsiteOnlyResult(
                    "Discord — Delivery could not be confirmed; this listing is website-only.",
                    showAccountLink: true,
                    showDiscordInvite: true);
                break;
        }

        PriceTextBox.IsEnabled = false;
        InGameNameTextBox.IsEnabled = false;
        SubmitButton.IsVisible = false;
        CancelButton.Content = "Close";
        ValidationTextBlock.Text = string.Empty;
    }

    private void ShowWebsiteOnlyResult(
        string discordStatus,
        bool showAccountLink,
        bool showDiscordInvite)
    {
        DiscordStatusTextBlock.Text = discordStatus;
        DiscordHelpTextBlock.Text = "For future automatic Discord posts, make sure Discord is linked on your AFM account and that you have joined the AFM Discord server.";
        DiscordSetupButtonsPanel.IsVisible = true;
        AccountLinkButton.IsVisible = showAccountLink;
        DiscordInviteButton.IsVisible = showDiscordInvite;
    }

    private void ApplyFailedResult(string message)
    {
        DeliveryTitleTextBlock.Text = saleAction == SaleAction.UpdatePrice ? "Update not applied" : "Not posted";
        WebsiteStatusTextBlock.Text = saleAction == SaleAction.UpdatePrice
            ? "AFM website — Existing listing was not changed"
            : "AFM website — Not posted";
        DiscordStatusTextBlock.Text = "Discord — Not posted";
        DiscordHelpTextBlock.Text = saleAction == SaleAction.UpdatePrice
            ? "The existing listing is unchanged. Correct any problem and try again."
            : "Nothing was published. Correct any problem and try again.";
        DiscordSetupButtonsPanel.IsVisible = false;
        ValidationTextBlock.Text = message;
    }

    private void SetPostingState(bool posting)
    {
        isPosting = posting;
        PostingProgressBar.IsVisible = posting;
        SubmitButton.Content = posting ? GetPostingLabel() : submitButtonText;
        SubmitButton.IsEnabled = !posting;
        CancelButton.IsEnabled = !posting;
        CalculatorButton.IsEnabled = !posting && item?.CanViewInCalculator == true;
        if (!isPosted)
        {
            PriceTextBox.IsEnabled = !posting;
            InGameNameTextBox.IsEnabled = !posting;
        }
    }

    private static string? NormalizePrice(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 18
            || normalized.Any(character => character is < '0' or > '9'))
        {
            return null;
        }

        normalized = normalized.TrimStart('0');
        return normalized.Length == 0 ? null : normalized;
    }

    private static SaleAction GetSaleAction(LegendaryItemRowViewModel item)
    {
        if (!item.HasListing)
        {
            return SaleAction.List;
        }

        return item.IsActiveListing ? SaleAction.UpdatePrice : SaleAction.Relist;
    }

    private string GetPreviewLabel() => saleAction switch
    {
        SaleAction.UpdatePrice => "Price update preview",
        SaleAction.Relist => "Relisting preview",
        _ => "Listing preview"
    };

    private string GetCompletedLabel() => saleAction switch
    {
        SaleAction.UpdatePrice => "Updated just now",
        SaleAction.Relist => "Relisted just now",
        _ => "Listed just now"
    };

    private string GetPostingLabel() => saleAction switch
    {
        SaleAction.UpdatePrice => "Updating...",
        SaleAction.Relist => "Relisting...",
        _ => "Posting..."
    };

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!isPosting)
        {
            Close();
        }
    }

    private void CalculatorButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl(item?.CalculatorUrl);
    }

    private void AccountLinkButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl(AccountUrl);
    }

    private void DiscordInviteButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl(DiscordInviteUrl);
    }

    private void OpenDiscordPostButton_Click(object? sender, RoutedEventArgs e)
    {
        OpenUrl(discordMessageUrl);
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        e.Cancel = isPosting;
    }

    private static void OpenUrl(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private enum SaleAction
    {
        List,
        UpdatePrice,
        Relist
    }
}

public sealed record LegendarySaleInput(string PriceSilver, string InGameName);
