using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyPlayerJoinedEventHandler : EventPacketHandler<PartyPlayerJoinedEvent>
{
    private readonly CombatTrackerService combatTracker;

    public PartyPlayerJoinedEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.PartyPlayerJoined)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(PartyPlayerJoinedEvent value)
    {
        combatTracker.AddPartyMember(value.UserGuid, value.Username);
        return Task.CompletedTask;
    }
}
