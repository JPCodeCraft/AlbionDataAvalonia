using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PlayerCountsEventHandler : EventPacketHandler<PlayerCountsEvent>
{
    private readonly PlayerState playerState;
    private readonly AFMUploader aFMUploader;

    public PlayerCountsEventHandler(PlayerState playerState, AFMUploader aFMUploader) : base((int)EventCodes.PlayerCounts)
    {
        this.playerState = playerState;
        this.aFMUploader = aFMUploader;
    }

    protected override async Task OnActionAsync(PlayerCountsEvent value)
    {
        if (playerState.CheckOkToUpload() && playerState.AlbionServer is not null)
        {
            value.PlayerCount.Location = playerState.Location;
            value.PlayerCount.Server = playerState.AlbionServer;
            this.aFMUploader.UploadPlayerCount(value.PlayerCount);
        }
        await Task.CompletedTask;
    }
}
