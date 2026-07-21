using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class NewJournalItemEvent : BaseEvent
{
    private readonly long? objectId;
    private readonly int itemId;
    private readonly int quantity;
    private readonly string? crafterName;
    private readonly long estimatedMarketValue;
    private readonly long? blackMarketEstimatedMarketValue;
    private readonly long durability;
    private readonly int quality = 1;
    private readonly bool isAwakened;

    public NewItem? Item { get; }

    public NewJournalItemEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? objectIdValue))
            {
                objectId = objectIdValue.ToLong();
            }

            if (parameters.TryGetValue(1, out object? itemIdValue))
            {
                itemId = checked((int)itemIdValue.ToLong());
            }

            if (parameters.TryGetValue(2, out object? quantityValue))
            {
                quantity = checked((int)quantityValue.ToLong());
            }

            if (parameters.TryGetValue(4, out object? estimatedMarketValueValue))
            {
                estimatedMarketValue = estimatedMarketValueValue.ToLong() / 10000;
            }

            if (parameters.TryGetValue(5, out object? blackMarketEstimatedMarketValueValue))
            {
                var parsedBlackMarketEstimatedMarketValue = blackMarketEstimatedMarketValueValue.ToLong() / 10000;
                blackMarketEstimatedMarketValue = parsedBlackMarketEstimatedMarketValue > 0 ? parsedBlackMarketEstimatedMarketValue : null;
            }

            if (parameters.TryGetValue(6, out object? crafterNameValue))
            {
                crafterName = crafterNameValue.ToString();
            }

            // Journal item param 7 is not confirmed yet. It may be durability,
            // but it is not parsed until the packet shape is verified.

            // Journal item param 8 appears to be fame, which we do not use yet.

            if (objectId != null)
            {
                Item = new NewItem(
                    objectId.Value,
                    itemId,
                    quantity,
                    durability,
                    estimatedMarketValue,
                    blackMarketEstimatedMarketValue,
                    quality,
                    crafterName,
                    isAwakened);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
