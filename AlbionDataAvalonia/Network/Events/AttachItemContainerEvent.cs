using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class AttachItemContainerEvent : BaseEvent
    {
        public long ObjectId { get; private set; }
        public Guid ContainerId { get; private set; } = Guid.Empty;
        public Guid PrivateContainerId { get; private set; } = Guid.Empty;
        public long[] SlotItems { get; private set; } = new long[128];

        public AttachItemContainerEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object? objectId))
                {
                    ObjectId = objectId.ToLong();
                }

                if (parameters.TryGetValue(1, out object? containerId))
                {
                    ContainerId = containerId.ToGuid() ?? Guid.Empty;
                }

                if (parameters.TryGetValue(2, out object? privateContainerId))
                {
                    PrivateContainerId = privateContainerId.ToGuid() ?? Guid.Empty;
                }

                if (parameters.TryGetValue(3, out object? slotsItems))
                {
                    SlotItems = slotsItems.ToLongArray() ?? new long[128];
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
