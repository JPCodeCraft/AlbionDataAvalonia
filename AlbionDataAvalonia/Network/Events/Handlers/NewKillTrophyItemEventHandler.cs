using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewKillTrophyItemEventHandler : EventPacketHandler<NewKillTrophyItemEvent>
{
    private readonly ItemsIdsService itemsIdsService;

    public NewKillTrophyItemEventHandler(ItemsIdsService itemsIdsService) : base((int)EventCodes.NewKillTrophyItem)
    {
        this.itemsIdsService = itemsIdsService;
    }

    protected override Task OnActionAsync(NewKillTrophyItemEvent value)
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