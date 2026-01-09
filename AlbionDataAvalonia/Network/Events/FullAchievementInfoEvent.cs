using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class FullAchievementInfoEvent : BaseEvent
    {
        public short[]? AchievementsIndexLevel100 { get; }
        public short[]? AchievementsIndex { get; }
        public byte[]? AchievementLevels { get; }
        public bool[]? Param4 { get; }
        public bool[]? Param5 { get; }

        public FullAchievementInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                AchievementsIndexLevel100 = GetShortArray(parameters, 1);
                AchievementsIndex = GetShortArray(parameters, 2);
                AchievementLevels = GetByteArray(parameters, 3);
                Param4 = GetBoolArray(parameters, 4);
                Param5 = GetBoolArray(parameters, 5);
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }

        private static short[]? GetShortArray(Dictionary<byte, object> parameters, byte key)
        {
            if (!parameters.TryGetValue(key, out object? value))
            {
                return null;
            }

            return value.ToShortArray();
        }

        private static byte[]? GetByteArray(Dictionary<byte, object> parameters, byte key)
        {
            if (!parameters.TryGetValue(key, out object? value))
            {
                return null;
            }

            return value.ToByteArray();
        }

        private static bool[]? GetBoolArray(Dictionary<byte, object> parameters, byte key)
        {
            if (!parameters.TryGetValue(key, out object? value))
            {
                return null;
            }

            return value.ToBoolArray();
        }
    }

}
