using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class RewardGrantedEventHandler : EventPacketHandler<RewardGrantedEvent>
{
    private readonly GatheringTrackerService gatheringTracker;

    public RewardGrantedEventHandler(GatheringTrackerService gatheringTracker) : base((int)EventCodes.RewardGranted)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(RewardGrantedEvent value)
    {
        gatheringTracker.ConfirmFishingReward(value.ItemId, value.Quantity);
        return Task.CompletedTask;
    }
}

