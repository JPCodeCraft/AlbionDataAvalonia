using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class EstimatedMarketValueUpdateEventHandler : EventPacketHandler<EstimatedMarketValueUpdateEvent>
{
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;

    public EstimatedMarketValueUpdateEventHandler(ItemsIdsService itemsIdsService, AFMUploader afmUploader) : base((int)EventCodes.EstimatedMarketValueUpdate)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
    }

    protected override Task OnActionAsync(EstimatedMarketValueUpdateEvent value)
    {
        foreach (var entry in value.Entries)
        {
            var itemData = itemsIdsService.GetItemById(entry.ItemId);
            entry.ItemUniqueName = itemData.UniqueName;
            entry.ItemUsName = itemData.UsName;

            afmUploader.QueueItemEstimatedMarketValue(
                entry.ItemUniqueName,
                entry.EstimatedMarketValue,
                entry.Quality);
        }

        return Task.CompletedTask;
    }
}
