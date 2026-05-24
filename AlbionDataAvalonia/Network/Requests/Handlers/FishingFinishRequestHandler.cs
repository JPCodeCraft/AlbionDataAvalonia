using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class FishingFinishRequestHandler : RequestPacketHandler<FishingFinishRequest>
{
    private readonly GatheringTrackerService gatheringTracker;

    public FishingFinishRequestHandler(GatheringTrackerService gatheringTracker) : base((int)OperationCodes.FishingFinish)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(FishingFinishRequest value)
    {
        gatheringTracker.MarkFishingSucceeded(value.Succeeded);
        return Task.CompletedTask;
    }
}

