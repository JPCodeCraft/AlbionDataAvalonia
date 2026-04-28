using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewCharacterEventHandler : EventPacketHandler<NewCharacterEvent>
{
    private readonly CombatTrackerService combatTracker;

    public NewCharacterEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.NewCharacter)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(NewCharacterEvent value)
    {
        if (value.ObjectId is not null || value.Guid is not null)
        {
            combatTracker.AddOrUpdatePlayer(value.ObjectId, value.Guid, value.Name);
        }

        return Task.CompletedTask;
    }
}
