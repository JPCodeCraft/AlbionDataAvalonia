using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class PartySetRoleFlagEventHandler : EventPacketHandler<PartySetRoleFlagEvent>
{
    private readonly PartyTrackerService partyTracker;

    public PartySetRoleFlagEventHandler(PartyTrackerService partyTracker) : base((int)EventCodes.PartySetRoleFlag)
    {
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(PartySetRoleFlagEvent value)
    {
        partyTracker.EnsurePartyMember(value.UserGuid);
        return Task.CompletedTask;
    }
}
