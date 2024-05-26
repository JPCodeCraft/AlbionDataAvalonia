using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace AlbionDataAvalonia.ViewModels;

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;

    [ObservableProperty]
    private AlbionServer? playerServer;

    [ObservableProperty]
    private List<AlbionMail> mails = new();

    public MailsViewModel()
    {
    }

    public MailsViewModel(SettingsManager settingsManager, PlayerState playerState, MailService mailService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _mailService = mailService;

        _playerState.OnPlayerStateChanged += async (sender, args) =>
        {
            PlayerServer = _playerState.AlbionServer;
            if (PlayerServer != null)
            {
                Mails = await _mailService.GetMails(PlayerServer.Id, 50, 0);
            }
        };
    }

}
