using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Events;

public abstract class VaultInfoEvent : BaseEvent
{
    public long? ObjectId { get; }
    public string LocationGuid { get; } = string.Empty;
    public Guid[] VaultGuidList { get; } = Array.Empty<Guid>();
    public string[] VaultNames { get; } = Array.Empty<string>();
    public string[] IconTags { get; } = Array.Empty<string>();
    public int[] VaultColors { get; } = Array.Empty<int>();

    protected VaultInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId)) ObjectId = objectId.ToLong();
            if (parameters.TryGetValue(1, out var locationGuid)) LocationGuid = locationGuid.ToString() ?? string.Empty;
            if (parameters.TryGetValue(2, out var vaultIds)) VaultGuidList = vaultIds.ToGuidArray();
            if (parameters.TryGetValue(3, out var vaultNames)) VaultNames = vaultNames.ToStringArray();
            if (parameters.TryGetValue(4, out var iconTags)) IconTags = iconTags.ToStringArray();
            if (parameters.TryGetValue(5, out var vaultColors)) VaultColors = vaultColors.ToIntArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse {EventName} event", GetType().Name);
        }
    }
}

public sealed class BankVaultInfoEvent : VaultInfoEvent
{
    public BankVaultInfoEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
    }
}
