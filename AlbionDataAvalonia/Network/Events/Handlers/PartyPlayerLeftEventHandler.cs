using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyPlayerLeftEventHandler : EventPacketHandler<PartyPlayerLeftEvent>
{
    private readonly CombatTrackerService combatTracker;

    public PartyPlayerLeftEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.PartyPlayerLeft)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(PartyPlayerLeftEvent value)
    {
        combatTracker.RemovePartyMember(value.UserGuid);
        return Task.CompletedTask;
    }
}
