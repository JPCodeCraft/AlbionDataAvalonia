using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Combat.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class UpdateFameEventHandler : EventPacketHandler<UpdateFameEvent>
{
    private readonly CombatTrackerService combatTracker;

    public UpdateFameEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.UpdateFame)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(UpdateFameEvent value)
    {
        if (CombatFameEvent.TryCreate(value.TotalGainedFame, out var fameEvent))
        {
            combatTracker.Record(fameEvent, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }
}
