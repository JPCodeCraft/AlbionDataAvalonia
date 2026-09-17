using AlbionDataAvalonia.Farming;
using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class LeaveEventHandler : EventPacketHandler<LeaveEvent>
{
    private readonly FarmingTrackerService farmingTracker;
    private readonly PlayerState playerState;

    public LeaveEventHandler(PlayerState playerState, FarmingTrackerService farmingTracker) : base((int)EventCodes.Leave)
    {
        this.playerState = playerState;
        this.farmingTracker = farmingTracker;
    }

    protected override Task OnActionAsync(LeaveEvent value)
    {
        farmingTracker.OnLeave(value.userObjectId);
        if (value.userObjectId == playerState.UserObjectId)
        {
            playerState.PlayerName = "Not set";
            playerState.Location = AlbionLocations.Unset;
            playerState.ResetPremiumStatus();
        }
        return Task.CompletedTask;
    }
}
