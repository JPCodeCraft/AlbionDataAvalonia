using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.Input;
using System;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    public bool CanOpenMapWindow => MapWindow is not null;
    public string MapWindowButtonText => MapWindow?.ButtonText ?? "";

    private void AttachMapWindow(ActivityMapWindowService? service)
    {
        if (service is not null) service.IsOpenChanged += OnMapWindowOpenChanged;
    }

    private void OnMapWindowOpenChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(MapWindowButtonText));

    [RelayCommand] private void ToggleMapWindow() => MapWindow?.Toggle();

    private void DisposeMapWindow()
    {
        if (MapWindow is not null) MapWindow.IsOpenChanged -= OnMapWindowOpenChanged;
    }
}
