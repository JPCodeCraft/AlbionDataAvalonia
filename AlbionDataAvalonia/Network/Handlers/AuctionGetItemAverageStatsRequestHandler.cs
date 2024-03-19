using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetItemAverageStatsRequestHandler : RequestPacketHandler<AuctionGetItemAverageStatsRequest>
{
    PlayerState playerState;
    public AuctionGetItemAverageStatsRequestHandler(PlayerState playerState) : base((int)OperationCodes.AuctionGetItemAverageStats)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetItemAverageStatsRequest value)
    {
        if (!playerState.CheckLocationIDIsSet()) return;

        MarketHistoryInfo info = new MarketHistoryInfo();
        playerState.MarketHistoryIDLookup[value.messageID % playerState.CacheSize] = info;

        info.Quality = value.quality;
        info.Timescale = value.timescale;
        info.AlbionId = value.albionId;
        info.LocationID = ((int)playerState.Location).ToString();

        await Task.CompletedTask;
    }
}
