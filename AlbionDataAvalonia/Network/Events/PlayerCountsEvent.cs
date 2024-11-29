using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events
{
    public class PlayerCountsEvent : BaseEvent
    {
        public PlayerCount PlayerCount { get; set; }

        public PlayerCountsEvent(Dictionary<byte, object> parameters) : base(parameters)
        {
            Log.Verbose("Got {PacketType} packet.", GetType());
            try
            {
                PlayerCount = new PlayerCount();

                PlayerCount.DateTime = DateTime.UtcNow;

                if (parameters.TryGetValue(0, out object? nonFlaggedCountObject))
                {
                    if (nonFlaggedCountObject is byte nonFlaggedCountValue)
                    {
                        PlayerCount.NonFlaggedCount = nonFlaggedCountValue;
                    }
                    else
                    {
                        PlayerCount.NonFlaggedCount = null;
                    }
                }
                if (parameters.TryGetValue(1, out object? flaggedCountObject))
                {
                    if (flaggedCountObject is byte flaggedCountValue)
                    {
                        PlayerCount.FlaggedCount = flaggedCountValue;
                    }
                    else
                    {
                        PlayerCount.FlaggedCount = null;
                    }
                }
                if (parameters.TryGetValue(2, out object? isBzObject))
                {
                    if (isBzObject is bool isBzValue)
                    {
                        PlayerCount.IsBz = isBzValue;
                    }
                    else
                    {
                        PlayerCount.IsBz = false;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, e.Message);
            }
        }
    }
}
