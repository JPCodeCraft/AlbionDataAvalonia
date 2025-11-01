using System;
using System.Linq;
using System.Threading.Tasks;
using AlbionDataAvalonia.Shared;

namespace Albion.Network
{
    public abstract class EventPacketHandler<TEvent> : PacketHandler<EventPacket> where TEvent : BaseEvent
    {
        private readonly int[]? eventCodes;
        private readonly int? singleEventCode;
        private readonly bool isSingleCode;

        public EventPacketHandler(int eventCode)
        {
            this.singleEventCode = eventCode;
            this.isSingleCode = true;
        }

        public EventPacketHandler(int[] eventCodes)
        {
            this.eventCodes = eventCodes;
            this.isSingleCode = false;
        }

        protected abstract Task OnActionAsync(TEvent value);

        protected internal override Task OnHandleAsync(EventPacket packet)
        {
            Console.WriteLine($"EventPacketHandler: Received event with code {(EventCodes)packet.EventCode}");

            if (isSingleCode)
            {
                if (packet.EventCode != singleEventCode)
                {
                    return NextAsync(packet);
                }
            }
            else
            {
                if (eventCodes == null || !eventCodes.Contains(packet.EventCode))
                {
                    return NextAsync(packet);
                }
            }

            TEvent instance = (TEvent)Activator.CreateInstance(typeof(TEvent), packet.Parameters);

            return OnActionAsync(instance);
        }
    }
}