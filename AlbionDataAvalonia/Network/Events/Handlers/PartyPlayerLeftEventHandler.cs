using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyPlayerLeftEventHandler : EventPacketHandler<PartyPlayerLeftEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartyPlayerLeftEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartyPlayerLeft)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartyPlayerLeftEvent value)
    {
        partyTracker.RemovePartyMember(value.UserGuid);
        return Task.CompletedTask;
    }
}
