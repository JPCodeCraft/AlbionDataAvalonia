using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class InCombatStateUpdateEventHandler : EventPacketHandler<InCombatStateUpdateEvent>
{
    private readonly CombatTrackerService combatTracker;

    public InCombatStateUpdateEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.InCombatStateUpdate)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(InCombatStateUpdateEvent value)
    {
        if (value.ObjectId is { } objectId)
        {
            combatTracker.UpdateCombatState(objectId, value.InActiveCombat, value.InPassiveCombat, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }
}
