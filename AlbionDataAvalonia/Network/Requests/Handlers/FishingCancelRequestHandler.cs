using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class FishingCancelRequestHandler : RequestPacketHandler<FishingCancelRequest>
{
    private readonly GatheringTrackerService gatheringTracker;

    public FishingCancelRequestHandler(GatheringTrackerService gatheringTracker) : base((int)OperationCodes.FishingCancel)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(FishingCancelRequest value)
    {
        gatheringTracker.ScheduleFishingFinalization(DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
