using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;

    [ObservableProperty]
    private AlbionServer? playerServer;

    public ObservableCollection<AlbionMail> Mails { get; } = new();

    public MailsViewModel()
    {
    }

    public MailsViewModel(SettingsManager settingsManager, PlayerState playerState, MailService mailService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _mailService = mailService;

        _mailService.OnMailAdded += HandleMailAdded;
        _mailService.OnMailDataAdded += HandleMailDataAdded;

        _playerState.OnPlayerStateChanged += async (sender, args) =>
        {
            PlayerServer = _playerState.AlbionServer;
        };
    }

    [RelayCommand]
    public async Task LoadMails()
    {
        try
        {
            if (PlayerServer == null)
            {
                return;
            }

            Mails.Clear();
            foreach (var mail in await _mailService.GetMails(PlayerServer.Id, 50, 0))
            {
                Mails.Add(mail);
            }
        }
        catch
        {
            Log.Error("Failed to load mails");
        }
    }

    private async void HandleMailAdded(List<AlbionMail> mails)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var mail in mails)
            {
                //check if it fits the filters first!!
                Mails.Add(mail);
            }

            var sortedMails = Mails.OrderByDescending(x => x.Received).ToList();

            Mails.Clear();
            foreach (var sortedMail in sortedMails)
            {
                Mails.Add(sortedMail);
                if (Mails.Count >= 50)
                {
                    break;
                }
            }
        });
    }

    private async void HandleMailDataAdded(AlbionMail mail)
    {
        //check if it fits the filters first!!
        var oldMail = Mails.SingleOrDefault(x => x.Id == mail.Id);
        if (oldMail != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Mails.Remove(oldMail);
                Mails.Add(mail);

                var sortedMails = Mails.OrderByDescending(x => x.Received).ToList();

                Mails.Clear();
                foreach (var sortedMail in sortedMails)
                {
                    Mails.Add(sortedMail);
                    if (Mails.Count >= 50)
                    {
                        break;
                    }
                }
            });
        }
    }

}
