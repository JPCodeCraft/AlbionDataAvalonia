using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class PremiumChangedEventHandler : EventPacketHandler<PremiumChangedEvent>
{
    private readonly PlayerState playerState;

    public PremiumChangedEventHandler(PlayerState playerState) : base((int)EventCodes.PremiumChanged)
    {
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(PremiumChangedEvent value)
    {
        if (value.userObjectId != playerState.UserObjectId)
        {
            return Task.CompletedTask;
        }

        playerState.SetPremiumExpirationTicks(value.premiumExpirationTicks);
        return Task.CompletedTask;
    }
}
