using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewKillTrophyItemEventHandler : EventPacketHandler<NewKillTrophyItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;
    private readonly LootTrackerService lootTracker;

    public NewKillTrophyItemEventHandler(
        ItemsIdsService itemsIdsService,
        AFMUploader afmUploader,
        LootTrackerService lootTracker) : base((int)EventCodes.NewKillTrophyItem)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(NewKillTrophyItemEvent value)
    {
        if (value.Item is not null)
        {
            var itemData = itemsIdsService.GetItemById(value.Item.ItemIndex);
            value.Item.ItemUniqueName = itemData.UniqueName;
            value.Item.ItemUsName = itemData.UsName;

            if (value.Item.EstimatedMarketValue > 0)
            {
                afmUploader.QueueItemEstimatedMarketValue(
                    value.Item.ItemUniqueName,
                    value.Item.EstimatedMarketValue,
                    value.Item.Quality);
            }

            lootTracker.DiscoverItem(value.Item);
        }

        return Task.CompletedTask;
    }
}
