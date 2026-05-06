using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class EstimatedMarketValueUpdateEventHandler : EventPacketHandler<EstimatedMarketValueUpdateEvent>
{
    private readonly ItemsIdsService itemsIdsService;

    public EstimatedMarketValueUpdateEventHandler(ItemsIdsService itemsIdsService) : base((int)EventCodes.EstimatedMarketValueUpdate)
    {
        this.itemsIdsService = itemsIdsService;
    }

    protected override Task OnActionAsync(EstimatedMarketValueUpdateEvent value)
    {
        foreach (var entry in value.Entries)
        {
            var itemData = itemsIdsService.GetItemById(entry.ItemId);
            entry.ItemUniqueName = itemData.UniqueName;
            entry.ItemUsName = itemData.UsName;
        }

        return Task.CompletedTask;
    }
}