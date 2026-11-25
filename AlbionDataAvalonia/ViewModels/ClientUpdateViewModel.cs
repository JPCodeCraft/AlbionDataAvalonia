using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>
/// The Update now notice in the sidebar, the tray menu and Settings › About. Updates download in the background and
/// never install while the client runs: the player clicks Update now, or the update installs on the next start.
/// </summary>
public partial class ClientUpdateViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool isReady;

    [ObservableProperty]
    private string readyMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    private bool isInstalling;

    public string InstallButtonText => IsInstalling ? "Updating…" : "Update now";

    public ClientUpdateViewModel()
    {
        ClientUpdater.ReadyUpdateChanged += OnReadyUpdateChanged;
        Refresh();
    }

    private void OnReadyUpdateChanged()
    {
        if (UiDispatch.Repost(OnReadyUpdateChanged)) return;
        Refresh();
    }

    private void Refresh()
    {
        var ready = ClientUpdater.ReadyUpdate;
        IsReady = ready is not null;
        ReadyMessage = ready is null
            ? string.Empty
            : $"Version {ready.Version} installs the next time the client starts.";
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        IsInstalling = true;
        try
        {
            // Returns only when nothing was installed; otherwise the client closes and the installer opens the new version.
            await ClientUpdater.InstallReadyUpdateAsync();
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
