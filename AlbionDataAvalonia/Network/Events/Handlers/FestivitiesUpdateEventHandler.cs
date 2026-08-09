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
            Log.Warning("Festivities parsed from update, but current server is unknown. Upload skipped.");
            return Task.CompletedTask;
        }

        var upload = new FestivitiesUpload
        {
            ServerId = playerState.AlbionServer.Id
        };

        for (var index = 0; index < value.Kinds.Length; index++)
        {
            var uniqueName = value.UniqueNames[index]?.Trim();
            if (string.IsNullOrWhiteSpace(uniqueName))
            {
                Log.Warning(
                    "Festivities update contains an invalid unique name at index {Index}. Entire snapshot rejected.",
                    index);
                return Task.CompletedTask;
            }

            if (!TryCreateUtcDateTime(value.StartTimeTicks[index], out var startTime)
                || !TryCreateUtcDateTime(value.EndTimeTicks[index], out var endTime)
                || endTime <= startTime)
            {
                Log.Warning(
                    "Festivities update contains an invalid time window at index {Index}. Entire snapshot rejected.",
                    index);
                return Task.CompletedTask;
            }

            upload.Events.Add(new FestivitiesUploadEvent
            {
                Kind = value.Kinds[index],
                Category = value.Categories[index]?.Trim() ?? string.Empty,
                UniqueName = uniqueName,
                StartTime = startTime,
                EndTime = endTime
            });
        }

        afmUploader.UploadFestivities(upload);

        return Task.CompletedTask;
    }

    private static bool TryCreateUtcDateTime(long ticks, out DateTime dateTime)
    {
        if (ticks < DateTime.UnixEpoch.Ticks)
        {
            dateTime = default;
            return false;
        }

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
