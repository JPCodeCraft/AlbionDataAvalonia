using Albion.Network;
using Serilog;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class CharacterStatsEvent : BaseEvent
    {
        public Dictionary<byte, object> Parameters { get; }

        public CharacterStatsEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            Parameters = parameters;
        }
    }
}
