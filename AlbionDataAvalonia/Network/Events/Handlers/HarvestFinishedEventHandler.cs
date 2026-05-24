using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class HarvestFinishedEventHandler : EventPacketHandler<HarvestFinishedEvent>
{
    private readonly GatheringTrackerService gatheringTracker;

    public HarvestFinishedEventHandler(GatheringTrackerService gatheringTracker) : base((int)EventCodes.HarvestFinished)
    {
        this.gatheringTracker = gatheringTracker;
    }

    protected override Task OnActionAsync(HarvestFinishedEvent value)
    {
        gatheringTracker.RecordHarvest(
            value.UserObjectId,
            value.ItemId,
            value.StandardAmount,
            value.GatheringBonusAmount,
            value.PremiumBonusAmount,
            DateTime.UtcNow);

        return Task.CompletedTask;
    }
}

