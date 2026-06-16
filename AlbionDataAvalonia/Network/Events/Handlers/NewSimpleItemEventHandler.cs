using Albion.Network;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewSimpleItemEventHandler : EventPacketHandler<NewSimpleItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly GatheringTrackerService gatheringTracker;
    private readonly LootTrackerService lootTracker;

    public NewSimpleItemEventHandler(
        ItemsIdsService itemsIdsService,
        AFMUploader afmUploader,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        GatheringTrackerService gatheringTracker,
        LootTrackerService lootTracker) : base((int)EventCodes.NewSimpleItem)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.gatheringTracker = gatheringTracker;
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(NewSimpleItemEvent value)
    {
        if (value.Item is not null)
        {
            var itemData = itemsIdsService.GetItemById(value.Item.ItemIndex);
            value.Item.ItemUniqueName = itemData.UniqueName;
            value.Item.ItemUsName = itemData.UsName;

            if (value.Item.EstimatedMarketValue > 0)
            {
                itemEstimatedMarketValues.Update(
                    value.Item.ItemIndex,
                    value.Item.Quality,
                    value.Item.EstimatedMarketValue);

                afmUploader.QueueItemEstimatedMarketValue(
                    value.Item.ItemUniqueName,
                    value.Item.EstimatedMarketValue,
                    value.Item.Quality);
            }

            gatheringTracker.DiscoverFishingItem(value.Item);
            lootTracker.DiscoverItem(value.Item);
        }

        return Task.CompletedTask;
    }
}
