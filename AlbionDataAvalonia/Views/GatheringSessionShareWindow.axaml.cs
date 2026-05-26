using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class GatheringSessionShareWindow : Window
{
    private readonly GatheringSessionShareImageService shareImageService = new();
    private readonly string suggestedFileName = "gathering_session.png";

    public GatheringSessionShareWindow()
    {
        InitializeComponent();
        CopyImageButton.IsVisible = OperatingSystem.IsWindows();
    }

    public GatheringSessionShareWindow(GatheringSessionShareCardViewModel shareCard) : this()
    {
        ShareCard.DataContext = shareCard;
        suggestedFileName = $"gathering_session_{shareCard.StartedAtUtc.ToLocalTime():yyyyMMdd_HHmmss}.png";
    }

    public static async Task ShowAsync(Window owner, GatheringSessionShareCardViewModel shareCard)
    {
        var window = new GatheringSessionShareWindow(shareCard);
        await window.ShowDialog(owner);
    }

    private async void CopyImageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clipboard = Clipboard;
        if (clipboard is null)
        {
            SetStatus("Clipboard is not available.");
            return;
        }

        try
        {
            using var bitmap = await shareImageService.RenderPngBitmapAsync(ShareCard);
            await clipboard.SetBitmapAsync(bitmap);
            await clipboard.FlushAsync();
            SetStatus("Image copied.");
        }
        catch (Exception ex)
        {
            SetStatus($"Copy failed: {ex.Message}");
        }
    }

    private async void SavePngButton_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save gathering session image",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("PNG Images") { Patterns = new[] { "*.png" } }
            }
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            using var bitmap = await shareImageService.RenderPngBitmapAsync(ShareCard);
            bitmap.Save(stream);
            SetStatus("Image saved.");
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }
}
