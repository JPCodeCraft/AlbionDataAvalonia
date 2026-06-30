using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class GuildVaultInfoEventHandler : EventPacketHandler<GuildVaultInfoEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public GuildVaultInfoEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.GuildVaultInfo)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override Task OnActionAsync(GuildVaultInfoEvent value)
    {
        return legendaryTracker.ObserveVaultAsync(
            true,
            value.ObjectId,
            value.LocationGuid,
            value.VaultGuidList,
            value.VaultNames,
            value.IconTags,
            value.VaultColors);
    }
}
