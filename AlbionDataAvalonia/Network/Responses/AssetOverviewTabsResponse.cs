using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AssetOverviewTabsResponse : BaseOperation
{
    public readonly Guid[] _vaultIds = Array.Empty<Guid>();
    public readonly string[] _vaultLocations = Array.Empty<string>();
    public readonly int[] _itemCounts = Array.Empty<int>();
    public readonly long[] _totalValues = Array.Empty<long>();
    public AssetOverviewTabsResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(1, out object? vaultIds))
            {
                _vaultIds = vaultIds.ToGuidArray();
            }
            if (parameters.TryGetValue(2, out object? vaultLocations))
            {
                _vaultLocations = vaultLocations.ToStringArray();
            }
            if (parameters.TryGetValue(4, out object? itemCounts))
            {
                _itemCounts = itemCounts.ToIntArray();
            }
            if (parameters.TryGetValue(5, out object? totalValues))
            {
                _totalValues = totalValues.ToLongArray();
                _totalValues = Array.ConvertAll(_totalValues, value => value / 10000);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
