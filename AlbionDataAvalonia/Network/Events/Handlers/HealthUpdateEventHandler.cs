using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class HealthUpdateEventHandler : EventPacketHandler<HealthUpdateEvent>
{
    private readonly CombatTrackerService combatTracker;

    public HealthUpdateEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.HealthUpdate)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(HealthUpdateEvent value)
    {
        if (value.TryNormalize(out var healthEvent))
        {
            combatTracker.Record(healthEvent, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }
}
