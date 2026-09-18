using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class JoinResponseHandler : ResponsePacketHandler<JoinResponse>
{
    private readonly PlayerState playerState;
    private readonly PartyTrackerService partyTracker;
    private readonly PlayerIdentityService playerIdentityService;
    private readonly LootTrackerService lootTracker;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public JoinResponseHandler(
        PlayerState playerState,
        PartyTrackerService partyTracker,
        PlayerIdentityService playerIdentityService,
        LootTrackerService lootTracker,
        LegendaryItemTrackerService legendaryTracker) : base((int)OperationCodes.Join)
    {
        this.playerState = playerState;
        this.partyTracker = partyTracker;
        this.playerIdentityService = playerIdentityService;
        this.lootTracker = lootTracker;
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(JoinResponse value)
    {
        // Failed joins may contain no identity or location fields. Preserve the
        // shared player state as well as the farming state until a successful join.
        if (value.ReturnCode != 0) return;
        playerIdentityService.AddOrUpdate(
            playerState.AlbionServer?.Id,
            value.userObjectId,
            value.userGuid,
            value.playerName,
            value.guildName,
            value.allianceName);
        partyTracker.SetLocalPlayer(value.userObjectId, value.userGuid, value.playerName);
        lootTracker.ResetTransientState();
        await legendaryTracker.ResetTransientStateAsync();

    }
}
