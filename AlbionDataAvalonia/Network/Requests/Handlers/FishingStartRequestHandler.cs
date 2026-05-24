using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class FishingStartRequestHandler : RequestPacketHandler<FishingStartRequest>
{
    private readonly GatheringTrackerService gatheringTracker;

    public FishingStartRequestHandler(GatheringTrackerService gatheringTracker) : base((int)OperationCodes.FishingStart)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(FishingStartRequest value)
    {
        gatheringTracker.StartFishing(value.EventId, value.UsedRodObjectId, DateTime.UtcNow);
        return Task.CompletedTask;
    }
}

