using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewSimpleItemEventHandler : EventPacketHandler<NewSimpleItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;

    public NewSimpleItemEventHandler(ItemsIdsService itemsIdsService, AFMUploader afmUploader) : base((int)EventCodes.NewSimpleItem)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
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
                afmUploader.QueueItemEstimatedMarketValue(
                    value.Item.ItemUniqueName,
                    value.Item.EstimatedMarketValue,
                    value.Item.Quality);
            }
        }

        return Task.CompletedTask;
    }
}
