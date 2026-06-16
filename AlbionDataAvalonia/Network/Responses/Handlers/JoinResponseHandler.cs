using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class JoinResponseHandler : ResponsePacketHandler<JoinResponse>
{
    private readonly PlayerState playerState;
    private readonly AFMUploader afmUploader;
    private readonly PartyTrackerService partyTracker;
    private readonly LootTrackerService lootTracker;

    public JoinResponseHandler(
        PlayerState playerState,
        AFMUploader afmUploader,
        PartyTrackerService partyTracker,
        LootTrackerService lootTracker) : base((int)OperationCodes.Join)
    {
        this.playerState = playerState;
        this.afmUploader = afmUploader;
        this.partyTracker = partyTracker;
        this.lootTracker = lootTracker;
    }

    protected override async Task OnActionAsync(JoinResponse value)
    {
        playerState.UserObjectId = value.userObjectId;
        playerState.PlayerName = value.playerName;
        playerState.Location = value.playerLocation;
        partyTracker.SetLocalPlayer(value.userObjectId, value.userGuid, value.playerName);
        lootTracker.ResetTransientState();

        if (value.globalMultiplier.HasValue)
        {
            if (playerState.AlbionServer is null)
            {
                Log.Warning("Global multiplier parsed from join response, but current server is unknown. Upload skipped.");
            }
            else
            {
                afmUploader.UploadGlobalMultiplier(new GlobalMultiplierUpload
                {
                    ServerId = playerState.AlbionServer.Id,
                    GlobalMultiplier = value.globalMultiplier.Value
                });
            }
        }

        await Task.CompletedTask;
    }
}
