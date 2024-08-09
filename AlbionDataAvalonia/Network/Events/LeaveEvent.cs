using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class LeaveEvent : BaseEvent
    {

        public readonly int userObjectId;
        public LeaveEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                if (parameters.TryGetValue(0, out object objectId))
                {
                    userObjectId = (int)objectId;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
