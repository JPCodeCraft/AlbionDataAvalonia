using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewItemEventHandler : EventPacketHandler<NewItemEvent>
{
    private readonly PlayerState playerState;
    private readonly ItemsIdsService itemsIdsService;

    public NewItemEventHandler(PlayerState playerState, ItemsIdsService itemsIdsService) : base([(int)EventCodes.NewSimpleItem, (int)EventCodes.NewJournalItem, (int)EventCodes.NewLaborerItem, (int)EventCodes.NewEquipmentItem, (int)EventCodes.NewFurnitureItem, (int)EventCodes.NewKillTrophyItem, (int)EventCodes.NewSiegeBannerItem])
    {
        this.playerState = playerState;
        this.itemsIdsService = itemsIdsService;
    }

    protected override async Task OnActionAsync(NewItemEvent value)
    {
        if (value.Item is not null)
        {
            var itemData = itemsIdsService.GetItemById(value.Item.ItemIndex);
            value.Item.ItemUniqueName = itemData.UniqueName;
            value.Item.ItemUsName = itemData.UsName;
            playerState.AddNewItem(value.Item);
        }
        await Task.CompletedTask;
    }
}
