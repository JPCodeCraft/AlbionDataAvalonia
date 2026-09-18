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
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly LootTrackerService lootTracker;
    private readonly PlayerState playerState;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public NewEquipmentItemEventHandler(
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        LootTrackerService lootTracker,
        PlayerState playerState,
        LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.NewEquipmentItem)
    {
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.lootTracker = lootTracker;
        this.playerState = playerState;
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(NewEquipmentItemEvent value)
    {
        if (value.Item is not null)
        {
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

            }

            lootTracker.DiscoverItem(value.Item);
            if (value.Item.IsAwakened)
            {
                await legendaryTracker.ObserveItemAsync(value.Item);
            }
        }
    }
}
