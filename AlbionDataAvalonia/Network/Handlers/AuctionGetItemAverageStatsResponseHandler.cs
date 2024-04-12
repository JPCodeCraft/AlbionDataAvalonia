using Albion.Network;
using AlbionData.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetItemAverageStatsResponseHandler : ResponsePacketHandler<AuctionGetItemAverageStatsResponse>
{
    private readonly Uploader uploader;
    private readonly PlayerState playerState;
    public AuctionGetItemAverageStatsResponseHandler(Uploader uploader, PlayerState playerState) : base((int)OperationCodes.AuctionGetItemAverageStats)
    {
        this.uploader = uploader;
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetItemAverageStatsResponse value)
    {
        if (!playerState.CheckOkToUpload()) return;

        MarketHistoriesUpload marketHistoriesUpload = new MarketHistoriesUpload();

        //load info from history
        MarketHistoryInfo info = playerState.MarketHistoryIDLookup[value.messageID % playerState.CacheSize];
        if (info == null)
        {
            Log.Warning("Market History - No info found for messageID {MessageID}. ", value.messageID);
            return;
        }

        //loops entries and fix amounts
        for (int i = 0; i < value.itemAmounts.Length; i++)
        {
            //sometimes opAuctionGetItemAverageStats receives negative item amounts
            if (value.itemAmounts[i] < 0)
            {
                if (value.itemAmounts[i] < 124)
                {
                    // still don't know what to do with these
                    Log.Warning("Market History - Ignoring negative item amount {Amount} for {Silver} silver on {Timestamp}",
                        value.itemAmounts[i], value.silverAmounts[i], value.timeStamps[i]);
                }
                // however these can be interpreted by adding them to 256
                // TODO: make more sense of this, (perhaps there is a better way)
                Log.Warning("Market History - Interpreting negative item amount {Amount} as {Amount} for {Silver} silver on {Timestamp}",
                    value.itemAmounts[i], value.itemAmounts[i] + 256, value.silverAmounts[i], value.timeStamps[i]);
                value.itemAmounts[i] = 256 + value.itemAmounts[i];
            }
            marketHistoriesUpload.MarketHistories.Add(new MarketHistory
            {
                ItemAmount = (ulong)value.itemAmounts[i],
                SilverAmount = value.silverAmounts[i],
                Timestamp = value.timeStamps[i]
            });
        }
        //fill the upload
        marketHistoriesUpload.AlbionId = info.AlbionId;
        marketHistoriesUpload.LocationId = ushort.Parse(info.LocationID);
        marketHistoriesUpload.QualityLevel = (byte)info.Quality;
        marketHistoriesUpload.Timescale = info.Timescale;

        if (marketHistoriesUpload.MarketHistories.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(null, null, marketHistoriesUpload));
        }
        await Task.CompletedTask;
    }
}
