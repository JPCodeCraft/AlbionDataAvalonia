using Albion.Network;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetOffersRequestHandler : RequestPacketHandler<AuctionGetOffersRequest>
{
    private readonly PlayerState playerState;
    public AuctionGetOffersRequestHandler(PlayerState playerState) : base((int)OperationCodes.AuctionGetOffers)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetOffersRequest value)
    {
        await Task.CompletedTask;
    }
}
