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
    private readonly ItemsIdsService itemsIdsService;
    private readonly AFMUploader afmUploader;
    private readonly ItemEstimatedMarketValueService itemEstimatedMarketValues;
    private readonly PlayerState playerState;

    public EstimatedMarketValueUpdateEventHandler(
        ItemsIdsService itemsIdsService,
        AFMUploader afmUploader,
        ItemEstimatedMarketValueService itemEstimatedMarketValues,
        PlayerState playerState) : base((int)EventCodes.EstimatedMarketValueUpdate)
    {
        this.itemsIdsService = itemsIdsService;
        this.afmUploader = afmUploader;
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
            var itemData = itemsIdsService.GetItemById(entry.ItemId);
            entry.ItemUniqueName = itemData.UniqueName;
            entry.ItemUsName = itemData.UsName;

            itemEstimatedMarketValues.Update(serverId.Value, entry.ItemId, entry.Quality, entry.EstimatedMarketValue);

            afmUploader.QueueItemEstimatedMarketValue(
                entry.ItemUniqueName,
                entry.EstimatedMarketValue,
                entry.Quality);
        }

        return Task.CompletedTask;
    }
}
