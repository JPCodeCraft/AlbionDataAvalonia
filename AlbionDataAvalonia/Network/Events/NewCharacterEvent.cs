using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public class NewCharacterEvent : BaseEvent
{
    public long? ObjectId { get; }
    public Guid? Guid { get; }
    public string Name { get; } = string.Empty;
    public string? GuildName { get; }
    public string? AllianceName { get; }

    public NewCharacterEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out object? name))
            {
                Name = name?.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(7, out object? guid))
            {
                Guid = guid.ToGuid();
            }

            if (parameters.TryGetValue(8, out object? guildName))
            {
                GuildName = guildName?.ToString() ?? string.Empty;
            }

            if (parameters.TryGetValue(51, out object? allianceName))
            {
                AllianceName = allianceName?.ToString() ?? string.Empty;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
