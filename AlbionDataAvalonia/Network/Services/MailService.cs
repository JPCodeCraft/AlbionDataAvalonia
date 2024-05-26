using AlbionDataAvalonia.DB;
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
    private List<AlbionMail> Mails { get; set; } = new();

    public MailService(PlayerState playerState, SettingsManager settingsManager)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
    }

    public async Task<List<AlbionMail>> GetMails(int albionServerId, int countPerPage, int pageNumber, bool showDeleted = false, int? locationId = null, MailInfoType? type = null)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var query = db.AlbionMails.Where(x => x.AlbionServerId == albionServerId);

                if (locationId.HasValue)
                {
                    query = query.Where(x => x.LocationId == locationId);
                }

                if (type.HasValue)
                {
                    query = query.Where(x => x.Type == type);
                }

                if (!showDeleted)
                {
                    query = query.Where(x => !x.Deleted);
                }

                return (await query.AsNoTracking().Skip(countPerPage * pageNumber).Take(countPerPage).ToListAsync()).OrderByDescending(x => x.Received).ToList();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new List<AlbionMail>();
        }
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

                mail.SetData(mailString);

                await db.SaveChangesAsync();
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
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
