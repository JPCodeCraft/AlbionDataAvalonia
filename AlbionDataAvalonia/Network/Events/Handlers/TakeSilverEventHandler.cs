using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Combat.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class TakeSilverEventHandler : EventPacketHandler<TakeSilverEvent>
{
    private readonly CombatTrackerService combatTracker;

    public TakeSilverEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.TakeSilver)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(TakeSilverEvent value)
    {
        if (CombatSilverEvent.TryCreate(value.ObjectId, value.SilverGained, out var silverEvent))
        {
            combatTracker.Record(silverEvent, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }
}
