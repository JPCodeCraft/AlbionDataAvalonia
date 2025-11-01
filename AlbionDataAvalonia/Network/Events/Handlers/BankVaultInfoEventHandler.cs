using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class BankVaultInfoEventHandler : EventPacketHandler<BankVaultInfoEvent>
{
    private readonly PlayerState playerState;

    public BankVaultInfoEventHandler(PlayerState playerState) : base((int)EventCodes.BankVaultInfo)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(BankVaultInfoEvent value)
    {
        await Task.CompletedTask;
    }
}
