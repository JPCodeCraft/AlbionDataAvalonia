using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class RedZoneWorldMapEventHandler : EventPacketHandler<RedZoneWorldMapEvent>
{
    private readonly PlayerState playerState;
    private readonly Uploader uploader;

    public RedZoneWorldMapEventHandler(PlayerState playerState, Uploader uploader) : base((int)EventCodes.RedZoneWorldMapEvent)
    {
        this.playerState = playerState;
        this.uploader = uploader;
    }

    protected override Task OnActionAsync(RedZoneWorldMapEvent value)
    {
        if (!playerState.IsInGame || playerState.AlbionServer is null)
        {
            return Task.CompletedTask;
        }

        if (!playerState.TryMarkBanditEventSubmission())
        {
            return Task.CompletedTask;
        }

        Log.Information("Bandit event detected (Phase: {Phase}) ending at {EventTime}", value.Phase, value.EventTime);

        var upload = new BanditEventUpload
        {
            EventTime = value.EventTime,
            Phase = value.Phase
        };

        uploader.EnqueueUpload(new Upload(null, null, null, upload));

        return Task.CompletedTask;
    }
}
