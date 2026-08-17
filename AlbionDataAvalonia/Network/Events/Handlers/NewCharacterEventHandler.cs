using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewCharacterEventHandler : EventPacketHandler<NewCharacterEvent>
{
    private readonly CombatTrackerService combatTracker;
    private readonly PartyTrackerService partyTracker;
    private readonly PlayerIdentityService playerIdentityService;
    private readonly PlayerState playerState;

    public NewCharacterEventHandler(
        CombatTrackerService combatTracker,
        PartyTrackerService partyTracker,
        PlayerIdentityService playerIdentityService,
        PlayerState playerState) : base((int)EventCodes.NewCharacter)
    {
        this.combatTracker = combatTracker;
        this.partyTracker = partyTracker;
        this.playerIdentityService = playerIdentityService;
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(NewCharacterEvent value)
    {
        if (value.ObjectId is not null || value.Guid is not null)
        {
            combatTracker.AddOrUpdatePlayer(value.ObjectId, value.Guid, value.Name);
            partyTracker.UpdatePartyMemberName(value.Guid, value.Name, value.ObjectId);
        }

        playerIdentityService.AddOrUpdate(
            playerState.AlbionServer?.Id,
            value.ObjectId,
            value.Guid,
            value.Name,
            value.GuildName,
            value.AllianceName);

        return Task.CompletedTask;
    }
}
