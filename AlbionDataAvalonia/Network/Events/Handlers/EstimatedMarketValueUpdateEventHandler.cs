using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class EstimatedMarketValueUpdateEventHandler : EventPacketHandler<EstimatedMarketValueUpdateEvent>
{
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly PlayerState playerState;

    public EstimatedMarketValueUpdateEventHandler(
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        PlayerState playerState) : base((int)EventCodes.EstimatedMarketValueUpdate)
    {
        this.itemEstimatedMarketValues = itemEstimatedMarketValues;
        this.playerState = playerState;
    }

    protected override Task OnActionAsync(EstimatedMarketValueUpdateEvent value)
    {
        var serverId = playerState.AlbionServer?.Id;
        if (serverId is null)
        {
            Log.Debug("Skipping estimated market value update because server is not set. EntriesCount: {EntriesCount}.", value.Entries.Count);
            return Task.CompletedTask;
        }

        foreach (var entry in value.Entries)
        {
            itemEstimatedMarketValues.Update(serverId.Value, entry.ItemId, entry.Quality, entry.EstimatedMarketValue);

        }

        return Task.CompletedTask;
    }
}
