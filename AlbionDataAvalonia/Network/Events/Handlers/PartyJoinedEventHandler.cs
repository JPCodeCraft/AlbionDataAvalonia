using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyJoinedEventHandler : EventPacketHandler<PartyJoinedEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartyJoinedEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartyJoined)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartyJoinedEvent value)
    {
        partyTracker.SetPartySnapshot(value.PartyUsers);
        return Task.CompletedTask;
    }
}
