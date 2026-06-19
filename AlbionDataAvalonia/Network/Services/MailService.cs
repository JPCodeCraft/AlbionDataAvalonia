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
    private readonly ItemsIdsService _itemsIdsService;
    private List<AlbionMail> Mails { get; set; } = new();

    public Action<List<AlbionMail>>? OnMailAdded;
    public Action<AlbionMail>? OnMailDataAdded;

    public MailService(PlayerState playerState, SettingsManager settingsManager, ItemsIdsService itemsIdsService)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
        _itemsIdsService = itemsIdsService;
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

    public async Task<List<int>> GetDistinctLocationIds(int? albionServerId = null)
    {
        try
        {
            using var db = new LocalContext();
            var query = db.AlbionMails.Where(x => !x.Deleted);
            if (albionServerId.HasValue)
            {
                query = query.Where(x => x.AlbionServerId == albionServerId);
            }

            return await query.Select(x => x.LocationId).Distinct().ToListAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new List<int>();
        }
    }

    private void SetMailProperties(List<AlbionMail> mails)
    {
        foreach (var mail in mails)
        {
            mail.Location = AlbionLocations.ResolveStoredLocation(mail.RawLocationId, mail.LocationId);
            mail.Server = AlbionServers.GetAll().SingleOrDefault(x => x.Id == mail.AlbionServerId);
            mail.ItemName = _itemsIdsService.GetUsNameByUniqueName(mail.ItemId);
        }

        Log.Verbose("Set mail properties for {count} mails", mails.Count);
    }

    public async Task AddMails(List<AlbionMail> mails)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var incomingIds = mails.Select(x => x.Id).ToList();
                var existingMails = await db.AlbionMails
                    .Where(x => incomingIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id);
                var newMails = mails.Where(mail => !existingMails.ContainsKey(mail.Id)).ToList();
                var repairedMails = new List<AlbionMail>();
                var unknownLocationId = AlbionLocations.Unknown.IdInt ?? -2;

                if (newMails.Any())
                {
                    await db.AlbionMails.AddRangeAsync(newMails);
                }

                foreach (var mail in mails)
                {
                    if (!existingMails.TryGetValue(mail.Id, out var existingMail))
                    {
                        continue;
                    }

                    var repaired = false;
                    if (ShouldUpdateRawLocation(existingMail.RawLocationId, mail.RawLocationId, mail.LocationId, unknownLocationId))
                    {
                        existingMail.RawLocationId = mail.RawLocationId;
                        repaired = true;
                    }

                    // Keep LocationId in sync with RawLocationId, the source of truth. Only fall
                    // back to the incoming int when the stored raw value still can't be resolved
                    // (e.g. legacy rows saved before RawLocationId existed).
                    var resolvedLocationId = string.IsNullOrWhiteSpace(existingMail.RawLocationId)
                        ? existingMail.LocationId
                        : AlbionLocations.ResolveMarketLocationId(existingMail.RawLocationId);

                    if (resolvedLocationId == unknownLocationId && mail.LocationId != unknownLocationId)
                    {
                        resolvedLocationId = mail.LocationId;
                    }

                    if (existingMail.LocationId != resolvedLocationId)
                    {
                        existingMail.LocationId = resolvedLocationId;
                        repaired = true;
                    }

                    if (repaired)
                    {
                        repairedMails.Add(existingMail);
                    }
                }

                if (newMails.Any() || repairedMails.Any())
                {
                    await db.SaveChangesAsync();

                    SetMailProperties(newMails);
                    SetMailProperties(repairedMails);

                    OnMailAdded?.Invoke(newMails.Concat(repairedMails).ToList());

                    Log.Debug("Added {Count} new mails", newMails.Count);
                    Log.Debug("Repaired {Count} mail locations", repairedMails.Count);
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

                OnMailDataAdded?.Invoke(mail);

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

    private static bool ShouldUpdateRawLocation(string existingRawLocationId, string newRawLocationId, int newLocationId, int unknownLocationId)
    {
        if (string.IsNullOrWhiteSpace(newRawLocationId) || newLocationId == unknownLocationId)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existingRawLocationId))
        {
            return true;
        }

        if (existingRawLocationId == unknownLocationId.ToString())
        {
            return true;
        }

        return AlbionLocations.ResolveMarketLocationId(existingRawLocationId) == unknownLocationId;
    }
}
