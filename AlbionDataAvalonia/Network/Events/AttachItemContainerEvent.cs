using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class AttachItemContainerEvent : BaseEvent
    {
        private readonly long _objectId;
        private readonly Guid _containerId = Guid.Empty;
        private readonly Guid _privateContainerId = Guid.Empty;
        private readonly long[] _slotsItems = new long[128];

        public AttachItemContainerEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object? objectId))
                {
                    _objectId = objectId.ToLong();
                }

                if (parameters.TryGetValue(1, out object? containerId))
                {
                    _containerId = containerId.ToGuid() ?? Guid.Empty;
                }

                if (parameters.TryGetValue(2, out object? privateContainerId))
                {
                    _privateContainerId = privateContainerId.ToGuid() ?? Guid.Empty;
                }

                if (parameters.TryGetValue(3, out object? slotsItems))
                {
                    _slotsItems = slotsItems.ToLongArray() ?? new long[128];
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
