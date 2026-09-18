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

public class NewFurnitureItemEventHandler : EventPacketHandler<NewFurnitureItemEvent>
{
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly LootTrackerService lootTracker;
    private readonly PlayerState playerState;

    public NewFurnitureItemEventHandler(
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        LootTrackerService lootTracker,
        PlayerState playerState) : base((int)EventCodes.NewFurnitureItem)
    {
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.lootTracker = lootTracker;
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(NewFurnitureItemEvent value)
    {
        if (value.Item is not null)
        {
            if (value.Item.EstimatedMarketValue > 0)
            {
                var serverId = playerState.AlbionServer?.Id;
                if (serverId is null)
                {
                    Log.Debug("Skipping furniture item estimated market value update because server is not set. ItemUniqueName: {ItemUniqueName}. Quality: {Quality}. Emv: {Emv}.", value.Item.ItemUniqueName, value.Item.Quality, value.Item.EstimatedMarketValue);
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
        }

        return Task.CompletedTask;
    }
}
