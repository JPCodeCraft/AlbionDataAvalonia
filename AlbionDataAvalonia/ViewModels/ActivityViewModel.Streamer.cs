using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.Input;
using System;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private ActivityStreamerWindowService? streamerWindow;

    public bool CanOpenStreamerWindow => streamerWindow is not null;
    public string StreamerWindowButtonText => streamerWindow?.ButtonText ?? "";

    private void AttachStreamerWindow(ActivityStreamerWindowService? service)
    {
        if (service is null) return;
        streamerWindow = service;
        service.IsOpenChanged += OnStreamerWindowOpenChanged;
    }

    private void OnStreamerWindowOpenChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(StreamerWindowButtonText));

    [RelayCommand] private void ToggleStreamerWindow() => streamerWindow?.Toggle();

    private void DisposeStreamerWindow()
    {
        if (streamerWindow is not null) streamerWindow.IsOpenChanged -= OnStreamerWindowOpenChanged;
    }
}
