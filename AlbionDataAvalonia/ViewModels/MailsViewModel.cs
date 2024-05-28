using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
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

    [ObservableProperty]
    private string filterText = string.Empty;

    private ObservableCollection<AlbionMail> mails = new();
    public ObservableCollection<AlbionMail> Mails
    {
        get { return mails; }
        set { SetProperty(ref mails, value); }
    }

    private List<AlbionMail> UnfilteredMails { get; set; } = new();

    partial void OnFilterTextChanged(string? oldValue, string newValue) => FilterMails(newValue);

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

        _playerState.OnPlayerStateChanged += (sender, args) =>
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
            UnfilteredMails = await _mailService.GetMails(PlayerServer.Id, 50, 0);
            FilterMails(FilterText);
        }
        catch
        {
            Log.Error("Failed to load mails");
        }
    }

    private async void HandleMailAdded(List<AlbionMail> mails)
    {
        UnfilteredMails.AddRange(mails);
        FilterMails(FilterText);
    }

    private void HandleMailDataAdded(AlbionMail mail)
    {
        //check if it fits the filters first!!
        var oldMail = Mails.SingleOrDefault(x => x.Id == mail.Id);
        if (oldMail != null)
        {
            UnfilteredMails.Remove(oldMail);
            UnfilteredMails.Add(mail);
            FilterMails(FilterText);
        }
    }

    private void FilterMails(string text)
    {
        List<AlbionMail> filteredList;
        if (!string.IsNullOrEmpty(text))
        {
            filteredList = UnfilteredMails.Where(x => x.ItemName.Replace(" ", "").Contains(text.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            filteredList = UnfilteredMails;
        }
        Mails = new ObservableCollection<AlbionMail>(filteredList.OrderByDescending(x => x.Received).Take(50));
    }
}
