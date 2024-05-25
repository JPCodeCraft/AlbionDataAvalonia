using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Services;

public class MailService : IDisposable
{
    private readonly PlayerState _playerState;
    private readonly SettingsManager _settingsManager;
    private List<AlbionMail> Mails { get; set; } = new();

    public MailService(PlayerState playerState, SettingsManager settingsManager)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
        _playerState.OnPlayerStateChanged += OnPlayerStateChanged;
    }

    public void AddMail(AlbionMail mail)
    {
        using (var db = new LocalContext())
        {
            try
            {
                if (db.AlbionMails.Select(x => x.Id).Contains(mail.Id))
                {
                    return;
                }
                db.AlbionMails.Add(mail);
                db.SaveChanges();
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }

    public void AddMailData(long mailId, string mailString)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var mail = db.AlbionMails.Where(x => x.Id == mailId).SingleOrDefault();

                if (mail == null) return;

                mail.MailString = mailString;

                db.SaveChanges();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    private void OnPlayerStateChanged(object? sender, PlayerStateEventArgs e)
    {
    }


    public void Dispose()
    {
        _playerState.OnPlayerStateChanged -= OnPlayerStateChanged;
    }
}
