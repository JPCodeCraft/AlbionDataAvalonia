using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewCharacterEventHandler : EventPacketHandler<NewCharacterEvent>
{
    private readonly PartyTrackerService partyTracker;
    private readonly PlayerIdentityService playerIdentityService;
    private readonly PlayerState playerState;

    public NewCharacterEventHandler(
        PartyTrackerService partyTracker,
        PlayerIdentityService playerIdentityService,
        PlayerState playerState) : base((int)EventCodes.NewCharacter)
    {
        this.partyTracker = partyTracker;
        this.playerIdentityService = playerIdentityService;
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(NewCharacterEvent value)
    {
        if (value.ObjectId is not null || value.Guid is not null)
        {
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
