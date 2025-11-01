using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AttachItemContainerEventHandler : EventPacketHandler<AttachItemContainerEvent>
{
    private readonly PlayerState playerState;

    public AttachItemContainerEventHandler(PlayerState playerState) : base((int)EventCodes.AttachItemContainer)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AttachItemContainerEvent value)
    {
        // if (value.userObjectId == playerState.UserObjectId)
        // {
        //     playerState.PlayerName = "Not set";
        //     playerState.Location = AlbionLocations.Unset;
        // }
        await Task.CompletedTask;
    }
}
