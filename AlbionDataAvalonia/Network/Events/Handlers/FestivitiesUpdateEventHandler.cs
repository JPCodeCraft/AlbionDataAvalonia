using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class FestivitiesUpdateEventHandler : EventPacketHandler<FestivitiesUpdateEvent>
{
    private const byte CraftingBonusEventType = 2;

    private readonly PlayerState playerState;
    private readonly AFMUploader afmUploader;

    public FestivitiesUpdateEventHandler(PlayerState playerState, AFMUploader afmUploader)
        : base((int)EventCodes.FestivitiesUpdate)
    {
        this.playerState = playerState;
        this.afmUploader = afmUploader;
    }

    protected override Task OnActionAsync(FestivitiesUpdateEvent value)
    {
        if (!value.IsValid)
        {
            return Task.CompletedTask;
        }

        if (playerState.AlbionServer is null)
        {
            Log.Warning("Crafting bonuses parsed from festivities update, but current server is unknown. Upload skipped.");
            return Task.CompletedTask;
        }

        var upload = new CraftingBonusUpload
        {
            ServerId = playerState.AlbionServer.Id
        };

        for (var index = 0; index < value.EventTypes.Length; index++)
        {
            if (value.EventTypes[index] != CraftingBonusEventType
                || string.IsNullOrWhiteSpace(value.Scopes[index])
                || string.IsNullOrWhiteSpace(value.UniqueNames[index]))
            {
                continue;
            }

            if (!TryCreateUtcDateTime(value.StartTimeTicks[index], out var startTime)
                || !TryCreateUtcDateTime(value.EndTimeTicks[index], out var endTime)
                || endTime <= startTime)
            {
                continue;
            }

            upload.Entries.Add(new CraftingBonusUploadEntry
            {
                EventType = value.EventTypes[index],
                Scope = value.Scopes[index],
                UniqueName = value.UniqueNames[index],
                StartTime = startTime,
                EndTime = endTime
            });
        }

        if (upload.Entries.Count > 0)
        {
            afmUploader.UploadCraftingBonuses(upload);
        }

        return Task.CompletedTask;
    }

    private static bool TryCreateUtcDateTime(long ticks, out DateTime dateTime)
    {
        try
        {
            dateTime = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            dateTime = default;
            return false;
        }
    }
}
