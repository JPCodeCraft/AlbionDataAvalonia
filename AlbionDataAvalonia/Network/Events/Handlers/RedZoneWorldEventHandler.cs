using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class RedZoneWorldEventHandler : EventPacketHandler<RedZoneWorldEvent>
{
    private readonly PlayerState playerState;
    private readonly Uploader uploader;

    public RedZoneWorldEventHandler(PlayerState playerState, Uploader uploader) : base((int)EventCodes.RedZoneWorldEvent)
    {
        this.playerState = playerState;
        this.uploader = uploader;
    }

    protected override Task OnActionAsync(RedZoneWorldEvent value)
    {
        if (!playerState.IsInGame || playerState.AlbionServer is null)
        {
            return Task.CompletedTask;
        }

        if (!playerState.TryMarkBanditEventSubmission())
        {
            return Task.CompletedTask;
        }

        if (value.AdvanceNotice)
        {
            Log.Information("Bandit event detected starting at {EventTime}", value.EventTime);
        }
        else
        {
            Log.Information("Bandit event detected ending at {EventTime}", value.EventTime);
        }

        var upload = new BanditEventUpload
        {
            EventTime = value.EventTime,
            AdvanceNotice = value.AdvanceNotice
        };

        uploader.EnqueueUpload(new Upload(null, null, null, upload));

        return Task.CompletedTask;
    }
}
