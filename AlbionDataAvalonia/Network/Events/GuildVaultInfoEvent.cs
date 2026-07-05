using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public sealed class GuildVaultInfoEvent : VaultInfoEvent
{
    public GuildVaultInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
    }
}
