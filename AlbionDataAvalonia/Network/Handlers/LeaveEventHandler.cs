using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class LeaveEventHandler : EventPacketHandler<LeaveEvent>
{
    private readonly PlayerState playerState;

    public LeaveEventHandler(PlayerState playerState) : base((int)EventCodes.Leave)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(LeaveEvent value)
    {
        if (value.userObjectId == playerState.UserObjectId)
        {
            playerState.PlayerName = "Not set";
            playerState.Location = AlbionLocations.Unset;
        }
        await Task.CompletedTask;
    }
}
