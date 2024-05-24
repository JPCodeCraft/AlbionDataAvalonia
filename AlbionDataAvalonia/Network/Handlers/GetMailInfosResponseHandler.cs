using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class GetMailInfosResponseHandler : ResponsePacketHandler<GetMailInfosResponse>
{
    private readonly PlayerState playerState;
    public GetMailInfosResponseHandler(PlayerState playerState) : base((int)OperationCodes.GetMailInfos)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(GetMailInfosResponse value)
    {
        await Task.CompletedTask;
    }
}
