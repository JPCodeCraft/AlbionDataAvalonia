using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PartyDisbandedEventHandler : EventPacketHandler<PartyDisbandedEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartyDisbandedEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartyDisbanded)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartyDisbandedEvent value)
    {
        partyTracker.DisbandParty();
        return Task.CompletedTask;
    }
}
