using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class ReadMailResponseHandler : ResponsePacketHandler<ReadMailResponse>
{
    private readonly PlayerState playerState;
    public ReadMailResponseHandler(PlayerState playerState) : base((int)OperationCodes.ReadMail)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(ReadMailResponse value)
    {
        await Task.CompletedTask;
    }
}
