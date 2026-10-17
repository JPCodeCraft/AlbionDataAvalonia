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
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class MailService
{
    private readonly PlayerState _playerState;
    private readonly SettingsManager _settingsManager;
    private readonly ItemsIdsService _itemsIdsService;
    private List<AlbionMail> Mails { get; set; } = new();

    public Action<AlbionMail>? OnMailDataAdded;

    public MailService(PlayerState playerState, SettingsManager settingsManager, ItemsIdsService itemsIdsService)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
        _itemsIdsService = itemsIdsService;
    }

    public async Task<int> CountMailsOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        using var db = new LocalContext();
        return await db.AlbionMails.CountAsync(x => x.Received < cutoffUtc, cancellationToken);
    }

    public async Task<int> DeleteMailsOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        using var db = new LocalContext();
        var deleted = await db.AlbionMails.Where(x => x.Received < cutoffUtc).ExecuteDeleteAsync(cancellationToken);
        Log.Information("Deleted {Count} mails older than {CutoffUtc:O}", deleted, cutoffUtc);
        return deleted;
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
