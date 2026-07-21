using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewEquipmentItemEventHandler : EventPacketHandler<NewEquipmentItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly LootTrackerService lootTracker;
    private readonly PlayerState playerState;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public NewEquipmentItemEventHandler(
        ItemsIdsService itemsIdsService,
        AFMUploader afmUploader,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        LootTrackerService lootTracker,
        PlayerState playerState,
        LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.NewEquipmentItem)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.lootTracker = lootTracker;
        this.playerState = playerState;
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(NewEquipmentItemEvent value)
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
                    Log.Debug("Skipping equipment item estimated market value update because server is not set. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", value.Item.ItemUniqueName, value.Item.Quality, value.Item.EstimatedMarketValue);
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
            if (value.Item.IsAwakened)
            {
                await legendaryTracker.ObserveItemAsync(value.Item);
            }
        }
    }
}
