using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.Input;
using System;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private ActivityMapWindowService? mapWindow;

    public bool CanOpenMapWindow => mapWindow is not null;
    public string MapWindowButtonText => mapWindow?.ButtonText ?? "";
    // A session with a single run maps that run; with several, a run chip picks one.
    private Guid? MapRunId => SelectedSession?.Id ?? (SelectedPlaySession?.Runs is { Count: 1 } runs ? runs[0].Id : null);
    public bool CanShowRunOnMap => mapWindow is not null && MapRunId is not null;

    private void AttachMapWindow(ActivityMapWindowService? service)
    {
        if (service is null) return;
        mapWindow = service;
        service.IsOpenChanged += OnMapWindowOpenChanged;
    }

    private void OnMapWindowOpenChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(MapWindowButtonText));

    [RelayCommand] private void ToggleMapWindow() => mapWindow?.Toggle();

    [RelayCommand]
    private void ShowRunOnMap()
    {
        if (MapRunId is { } id) mapWindow?.ShowRun(id);
    }

    private void DisposeMapWindow()
    {
        if (mapWindow is not null) mapWindow.IsOpenChanged -= OnMapWindowOpenChanged;
    }
}
