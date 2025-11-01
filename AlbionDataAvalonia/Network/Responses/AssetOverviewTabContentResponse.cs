using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AssetOverviewTabContentResponse : BaseOperation
{
    public readonly Guid _vaultId;
    public readonly Guid[] _containerIds = Array.Empty<Guid>();
    public readonly string[] _containerNames = Array.Empty<string>();
    public readonly string[] _containerIcons = Array.Empty<string>();
    public readonly int[] _containerItemCounts = Array.Empty<int>();

    public AssetOverviewTabContentResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());
        try
        {
            if (parameters.TryGetValue(0, out object? vaultId))
            {
                _vaultId = vaultId.ToGuid() ?? Guid.Empty;
            }
            if (parameters.TryGetValue(1, out object? containerIds))
            {
                _containerIds = containerIds.ToGuidArray();
            }
            if (parameters.TryGetValue(2, out object? containerNames))
            {
                _containerNames = containerNames.ToStringArray();
            }
            if (parameters.TryGetValue(3, out object? containerIcons))
            {
                _containerIcons = containerIcons.ToStringArray();
            }
            if (parameters.TryGetValue(5, out object? containerItemCounts))
            {
                _containerItemCounts = containerItemCounts.ToIntArray();
            }

        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
