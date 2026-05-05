using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class UpdateFameEventHandler : EventPacketHandler<UpdateFameEvent>
{
    public UpdateFameEventHandler() : base((int)EventCodes.UpdateFame)
    {
    }

    protected override Task OnActionAsync(UpdateFameEvent value)
    {
        return Task.CompletedTask;
    }
}
