using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewEquipmentItemEventHandler : EventPacketHandler<NewEquipmentItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;

    public NewEquipmentItemEventHandler(ItemsIdsService itemsIdsService) : base((int)EventCodes.NewEquipmentItem)
    {
        this.itemsIdsService = itemsIdsService;
    }

    protected override Task OnActionAsync(NewEquipmentItemEvent value)
    {
        if (value.Item is not null)
        {
            var itemData = itemsIdsService.GetItemById(value.Item.ItemIndex);
            value.Item.ItemUniqueName = itemData.UniqueName;
            value.Item.ItemUsName = itemData.UsName;
        }

        return Task.CompletedTask;
    }
}