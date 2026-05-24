using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class FishingFinishResponseHandler : ResponsePacketHandler<FishingFinishResponse>
{
    private readonly GatheringTrackerService gatheringTracker;

    public FishingFinishResponseHandler(GatheringTrackerService gatheringTracker) : base((int)OperationCodes.FishingFinish)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(FishingFinishResponse value)
    {
        gatheringTracker.ScheduleFishingFinalization(DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
