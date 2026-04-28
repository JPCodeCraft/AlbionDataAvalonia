using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Combat.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class HealthUpdatesEventHandler : EventPacketHandler<HealthUpdatesEvent>
{
    private readonly CombatTrackerService combatTracker;

    public HealthUpdatesEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.HealthUpdates)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(HealthUpdatesEvent value)
    {
        var receivedAtUtc = DateTime.UtcNow;
        var healthEvents = new List<CombatHealthEvent>();
        foreach (var update in value.HealthUpdates)
        {
            if (update.TryNormalize(out var healthEvent))
            {
                healthEvents.Add(healthEvent);
            }
        }

        combatTracker.RecordBatch(healthEvents, receivedAtUtc);
        return Task.CompletedTask;
    }
}
