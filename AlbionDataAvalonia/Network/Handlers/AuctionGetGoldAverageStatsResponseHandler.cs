using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetGoldAverageStatsResponseHandler : ResponsePacketHandler<AuctionGetGoldAverageStatsResponse>
{
    private readonly Uploader uploader;
    public AuctionGetGoldAverageStatsResponseHandler(Uploader uploader) : base((int)OperationCodes.GoldMarketGetAverageInfo)
    {
        this.uploader = uploader;
    }

    protected override async Task OnActionAsync(AuctionGetGoldAverageStatsResponse value)
    {
        GoldPriceUpload goldHistoriesUpload = new();

        goldHistoriesUpload.Prices = value.prices;
        goldHistoriesUpload.Timestamps = value.timeStamps;

        if (goldHistoriesUpload.Prices.Count() > 0 && goldHistoriesUpload.Prices.Count() == goldHistoriesUpload.Timestamps.Count())
        {
            uploader.EnqueueUpload(new Upload(null, goldHistoriesUpload, null));
        }
        await Task.CompletedTask;
    }
}
