using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewCharacterEventHandler : EventPacketHandler<NewCharacterEvent>
{
    private readonly CombatTrackerService combatTracker;
    private readonly PartyTrackerService partyTracker;

    public NewCharacterEventHandler(CombatTrackerService combatTracker, PartyTrackerService partyTracker) : base((int)EventCodes.NewCharacter)
    {
        this.combatTracker = combatTracker;
        this.partyTracker = partyTracker;
    }

    protected override Task OnActionAsync(NewCharacterEvent value)
    {
        if (value.ObjectId is not null || value.Guid is not null)
        {
            combatTracker.AddOrUpdatePlayer(value.ObjectId, value.Guid, value.Name);
            partyTracker.UpdatePartyMemberName(value.Guid, value.Name, value.ObjectId);
        }

        return Task.CompletedTask;
    }
}
