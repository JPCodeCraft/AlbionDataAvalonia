using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class MailService
{
    private readonly PlayerState _playerState;
    private readonly SettingsManager _settingsManager;
    private readonly LocalizationService _localizationService;
    private List<AlbionMail> Mails { get; set; } = new();

    public Action<List<AlbionMail>> OnMailAdded;
    public Action<AlbionMail> OnMailDataAdded;

    public MailService(PlayerState playerState, SettingsManager settingsManager, LocalizationService localizationService)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
        _localizationService = localizationService;
    }

    public async Task<List<AlbionMail>> GetMails(int countPerPage, int pageNumber = 0, int? albionServerId = null, bool showDeleted = false, int? locationId = null, AuctionType? auctionType = null)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var query = db.AlbionMails.AsQueryable();

                if (albionServerId.HasValue)
                {
                    query = query.Where(x => x.AlbionServerId == albionServerId);
                }

                if (locationId.HasValue)
                {
                    query = query.Where(x => x.LocationId == locationId);
                }

                if (auctionType != null)
                {
                    query = query.Where(x => x.AuctionType == auctionType);
                }

                if (!showDeleted)
                {
                    query = query.Where(x => !x.Deleted);
                }

                var result = await query.OrderByDescending(x => x.Received).AsNoTracking().Skip(countPerPage * pageNumber).Take(countPerPage).ToListAsync();

                SetMailProperties(result);

                Log.Debug("Loaded {Count} mails", result.Count);

                return result;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new List<AlbionMail>();
        }
    }

    private void SetMailProperties(List<AlbionMail> mails)
    {
        foreach (var mail in mails)
        {
            mail.Location = AlbionLocations.GetByIntId(mail.LocationId);
            mail.Server = AlbionServers.GetAll().SingleOrDefault(x => x.Id == mail.AlbionServerId);
            mail.ItemName = _localizationService.GetUsName(mail.ItemId);
        }

        Log.Verbose("Set mail properties for {count} mails", mails.Count);
    }

    public async Task AddMails(List<AlbionMail> mails)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var existingMailIds = await db.AlbionMails.Select(x => x.Id).ToListAsync();
                var newMails = mails.Where(mail => !existingMailIds.Contains(mail.Id)).ToList();

                if (newMails.Any())
                {
                    await db.AlbionMails.AddRangeAsync(newMails);
                    await db.SaveChangesAsync();

                    SetMailProperties(newMails);

                    OnMailAdded.Invoke(newMails);

                    Log.Debug("Added {Count} new mails", newMails.Count);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public async Task AddMailData(long mailId, string mailString)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var mail = await db.AlbionMails.Where(x => x.Id == mailId).SingleOrDefaultAsync();

                if (mail == null) return;

                if (mail.IsSet)
                {
                    SetMailProperties(new List<AlbionMail>([mail]));
                    Log.Debug("Mail data already set for mail {MailId}", mailId);
                    return;
                }

                mail.SetData(mailString);

                await db.SaveChangesAsync();

                SetMailProperties(new List<AlbionMail>([mail]));

                OnMailDataAdded.Invoke(mail);

                Log.Debug("Added data for mail {MailId}", mailId);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public async Task DeleteMail(long mailId)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var mail = await db.AlbionMails.Where(x => x.Id == mailId).SingleOrDefaultAsync();

                if (mail == null) return;

                mail.Deleted = true;

                await db.SaveChangesAsync();

                Log.Debug("Deleted mail {MailId}", mailId);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
