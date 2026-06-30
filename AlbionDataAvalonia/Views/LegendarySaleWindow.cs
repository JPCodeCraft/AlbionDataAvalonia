using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class LegendarySaleWindow : Window
{
    private LegendarySaleInput? saleInput;

    private LegendarySaleWindow(
        LegendaryItemRowViewModel item,
        string? discordUsername,
        string defaultInGameName)
    {
        Title = "Post legendary item for sale";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var priceBox = new TextBox
        {
            Watermark = "Price in silver",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var inGameNameBox = new TextBox
        {
            Text = defaultInGameName,
            Watermark = "Albion character name",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var validation = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var post = new Button { Content = "Post to Discord", MinWidth = 130 };
        cancel.Click += (_, _) => Close();
        post.Click += (_, _) =>
        {
            var normalized = (priceBox.Text ?? string.Empty).Trim();
            if (normalized.Length is < 1 or > 18
                || normalized.Any(character => character is < '0' or > '9'))
            {
                validation.Text = "Enter a positive whole-silver price with at most 18 digits.";
                return;
            }
            normalized = normalized.TrimStart('0');
            if (normalized.Length == 0)
            {
                validation.Text = "Enter a positive whole-silver price with at most 18 digits.";
                return;
            }
            var inGameName = (inGameNameBox.Text ?? string.Empty).Trim();
            if (inGameName.Length is < 1 or > 100 || inGameName.Any(char.IsControl))
            {
                validation.Text = "Enter an in-game character name with at most 100 characters.";
                return;
            }
            saleInput = new LegendarySaleInput(normalized, inGameName);
            Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Post legendary item for sale", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.DemiBold },
                new TextBlock { Text = item.ItemName, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.DemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"{item.ServerName} • {item.Quality} • Rating {item.LegendaryRating}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = $"Attunement {item.Attunement} • Strain {item.Strain} • PvP fame {item.PvPFameGained} • Attunement spent {item.AttunementSpent}", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = item.TraitsSummary, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8 },
                new TextBlock { Text = $"Discord seller: {discordUsername ?? "linked account"}" },
                new TextBlock { Text = "In-game contact", FontWeight = Avalonia.Media.FontWeight.DemiBold },
                inGameNameBox,
                new TextBlock { Text = "Asking price", FontWeight = Avalonia.Media.FontWeight.DemiBold },
                priceBox,
                validation,
                new TextBlock
                {
                    Text = "The AFM bot will mention your linked Discord account and show this character as an in-game contact option. Item data is reported by this Data Client.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontSize = 11,
                    Opacity = 0.7
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, post }
                }
            }
        };
    }

    public static async Task<LegendarySaleInput?> ShowAsync(
        Window owner,
        LegendaryItemRowViewModel item,
        string? discordUsername,
        string defaultInGameName)
    {
        var window = new LegendarySaleWindow(item, discordUsername, defaultInGameName);
        await window.ShowDialog(owner);
        return window.saleInput;
    }

    public static async Task ShowResultAsync(Window owner, string message, string? messageUrl, string? inviteUrl)
    {
        var window = new Window
        {
            Title = messageUrl is null ? "Legendary Discord sale" : "Legendary item posted",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var targetUrl = messageUrl ?? inviteUrl;
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            var open = new Button { Content = messageUrl is null ? "Join AFM Discord" : "Open Discord post" };
            open.Click += (_, _) => OpenUrl(targetUrl);
            buttons.Children.Add(open);
        }
        buttons.Children.Add(close);
        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons
            }
        };
        await window.ShowDialog(owner);
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

public sealed record LegendarySaleInput(string PriceSilver, string InGameName);
