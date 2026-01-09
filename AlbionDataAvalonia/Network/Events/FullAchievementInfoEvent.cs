using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class FullAchievementInfoEvent : BaseEvent
    {
        private const int AchievementIndexOffset = 6;

        public short[]? Param1 { get; }
        public short[]? Param2 { get; }
        public int[]? AchievementIndices { get; }
        public int[]? AchievementLevel100Indices { get; }
        public byte[]? AchievementLevels { get; }
        public bool[]? Param4 { get; }
        public bool[]? Param5 { get; }

        public FullAchievementInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                Param1 = GetShortArray(parameters, 1);
                Param2 = GetShortArray(parameters, 2);
                AchievementIndices = BuildAchievementIndices(Param2);
                AchievementLevel100Indices = BuildAchievementIndices(Param1);
                AchievementLevels = GetByteArray(parameters, 3);
                Param4 = GetBoolArray(parameters, 4);
                Param5 = GetBoolArray(parameters, 5);
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }

        private static int[]? BuildAchievementIndices(short[]? indices)
        {
            if (indices is null)
            {
                return null;
            }

            var mapped = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                mapped[i] = indices[i] - AchievementIndexOffset;
            }

            return mapped;
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

    public readonly record struct AchievementInfo(string Id, byte Level);
}
