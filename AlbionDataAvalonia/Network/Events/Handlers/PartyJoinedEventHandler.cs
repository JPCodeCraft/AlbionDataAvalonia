using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyJoinedEventHandler : EventPacketHandler<PartyJoinedEvent>
{
    private readonly CombatTrackerService combatTracker;

    public PartyJoinedEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.PartyJoined)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(PartyJoinedEvent value)
    {
        combatTracker.SetPartySnapshot(value.PartyUsers);
        return Task.CompletedTask;
    }
}
