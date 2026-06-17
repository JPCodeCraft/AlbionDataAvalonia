using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class PartyOnClusterPartyJoinedEventHandler : EventPacketHandler<PartyOnClusterPartyJoinedEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartyOnClusterPartyJoinedEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartyOnClusterPartyJoined)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartyOnClusterPartyJoinedEvent value)
    {
        partyTracker.EnsurePartyMembers(value.UserGuids);
        return Task.CompletedTask;
    }
}
