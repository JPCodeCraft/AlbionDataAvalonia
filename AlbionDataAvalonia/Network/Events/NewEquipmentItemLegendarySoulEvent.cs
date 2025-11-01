using Albion.Network;
using Avalonia.Controls;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class NewEquipmentItemLegendarySoulEvent : BaseEvent
    {
        private readonly long? _objectId;
        private readonly long _attunement;
        private readonly double _strain;
        private readonly string[] traits_ids = Array.Empty<string>();
        private readonly double[] traits_values = Array.Empty<double>();

        public LegendarySoul? LegendarySoul { get; }

        public NewEquipmentItemLegendarySoulEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object? objectId))
                {
                    _objectId = objectId.ToLong();
                }

                if (parameters.TryGetValue(6, out object? strain))
                {
                    _strain = strain.ToInt() / 10000f;
                }

                if (parameters.TryGetValue(7, out object? attunement))
                {
                    _attunement = attunement.ToLong() / 10000;
                }

                if (parameters.TryGetValue(8, out object? traitsIds))
                {
                    traits_ids = traitsIds.ToStringArray();
                }

                if (parameters.TryGetValue(9, out object? traitsValues))
                {
                    traits_values = traitsValues.ToDoubleArray();
                }

                if (_objectId != null)
                {
                    LegendarySoul = new LegendarySoul(
                        _objectId.Value,
                        _attunement,
                        _strain, traits_ids, traits_values);
                }
                else
                {
                    LegendarySoul = null;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
