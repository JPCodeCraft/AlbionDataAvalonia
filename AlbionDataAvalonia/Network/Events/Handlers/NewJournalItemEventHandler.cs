using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewJournalItemEventHandler : EventPacketHandler<NewJournalItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly LootTrackerService lootTracker;
    private readonly PlayerState playerState;

    public NewJournalItemEventHandler(
        ItemsIdsService itemsIdsService,
        AFMUploader afmUploader,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        LootTrackerService lootTracker,
        PlayerState playerState) : base((int)EventCodes.NewJournalItem)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.lootTracker = lootTracker;
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(NewJournalItemEvent value)
    {
        if (value.Item is not null)
        {
            var itemData = itemsIdsService.GetItemById(value.Item.ItemIndex);
            value.Item.ItemUniqueName = itemData.UniqueName;
            value.Item.ItemUsName = itemData.UsName;

            if (value.Item.EstimatedMarketValue > 0)
            {
                var serverId = playerState.AlbionServer?.Id;
                if (serverId is null)
                {
                    Log.Debug("Skipping journal item estimated market value update because server is not set. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", value.Item.ItemUniqueName, value.Item.Quality, value.Item.EstimatedMarketValue);
                }
                else
                {
                    itemEstimatedMarketValues.Update(
                        serverId.Value,
                        value.Item.ItemIndex,
                        value.Item.Quality,
                        value.Item.EstimatedMarketValue,
                        value.Item.BlackMarketEstimatedMarketValue);
                }

                afmUploader.QueueItemEstimatedMarketValue(
                    value.Item.ItemUniqueName,
                    value.Item.EstimatedMarketValue,
                    value.Item.Quality,
                    value.Item.BlackMarketEstimatedMarketValue);
            }

            lootTracker.DiscoverItem(value.Item);
        }

        return Task.CompletedTask;
    }
}
