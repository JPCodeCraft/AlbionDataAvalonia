using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewEquipmentItemLegendarySoulEventHandler : EventPacketHandler<NewEquipmentItemLegendarySoulEvent>
{
    private readonly PlayerState playerState;

    public NewEquipmentItemLegendarySoulEventHandler(PlayerState playerState) : base((int)EventCodes.NewEquipmentItemLegendarySoul)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(NewEquipmentItemLegendarySoulEvent value)
    {
        if (value.LegendarySoul is not null)
        {
            playerState.AddLegendarySoul(value.LegendarySoul);
        }
        await Task.CompletedTask;
    }
}
