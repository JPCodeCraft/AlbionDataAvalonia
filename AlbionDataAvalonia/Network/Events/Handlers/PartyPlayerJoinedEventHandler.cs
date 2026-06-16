using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyPlayerJoinedEventHandler : EventPacketHandler<PartyPlayerJoinedEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartyPlayerJoinedEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartyPlayerJoined)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartyPlayerJoinedEvent value)
    {
        partyTracker.AddPartyMember(value.UserGuid, value.Username);
        return Task.CompletedTask;
    }
}
