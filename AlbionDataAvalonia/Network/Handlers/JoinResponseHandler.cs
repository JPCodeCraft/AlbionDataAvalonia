using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class JoinResponseHandler : ResponsePacketHandler<JoinResponse>
{
    private readonly PlayerState playerState;
    public JoinResponseHandler(PlayerState playerState) : base((int)OperationCodes.Join)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(JoinResponse value)
    {
        playerState.PlayerName = value.playerName;
        playerState.Location = value.playerLocation;
        await Task.CompletedTask;
    }
}
