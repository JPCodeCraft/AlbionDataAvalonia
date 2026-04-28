using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyDisbandedEventHandler : EventPacketHandler<PartyDisbandedEvent>
{
    private readonly CombatTrackerService combatTracker;

    public PartyDisbandedEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.PartyDisbanded)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(PartyDisbandedEvent value)
    {
        combatTracker.DisbandParty();
        return Task.CompletedTask;
    }
}
