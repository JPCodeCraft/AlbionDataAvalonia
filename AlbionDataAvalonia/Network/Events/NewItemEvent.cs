using Albion.Network;
using Avalonia.Controls;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class NewItemEvent : BaseEvent
    {
        private readonly long? _objectId;
        private readonly int _itemId;
        private readonly int _quantity;
        private readonly string? _crafterName;
        private readonly long _estimatedMarketValue;
        private readonly long _durability;
        private readonly int _quality;
        private readonly bool _isAwakened = false;

        public NewItem? Item { get; }

        public NewItemEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object? objectId))
                {
                    _objectId = objectId.ToLong();
                }

                if (parameters.TryGetValue(1, out object? itemId))
                {
                    _itemId = itemId.ToInt();
                }

                if (parameters.TryGetValue(2, out object? quantity))
                {
                    _quantity = quantity.ToInt();
                }

                if (parameters.TryGetValue(4, out object? estimatedMarketValue))
                {
                    _estimatedMarketValue = estimatedMarketValue.ToLong() / 10000;
                }

                if (parameters.TryGetValue(5, out object? crafterName))
                {
                    _crafterName = crafterName.ToString();
                }

                if (parameters.TryGetValue(6, out object? quality))
                {
                    _quality = quality.ToInt();
                }

                if (parameters.TryGetValue(7, out object? durability))
                {
                    _durability = durability.ToLong() / 10000;
                }
                if (parameters.TryGetValue(10, out object? isAwakened))
                {
                    _isAwakened = isAwakened.ToBool();
                }

                if (_objectId != null)
                {
                    Item = new NewItem(
                        _objectId.Value,
                        _itemId,
                        _quantity,
                        _durability,
                        _estimatedMarketValue,
                        _quality,
                        _crafterName,
                        _isAwakened);
                }
                else
                {
                    Item = null;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
