using Albion.Network;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Responses;

public class AssetOverviewUnfreezeCacheResponse : BaseOperation
{
    public readonly Guid _containerId;
    public readonly int[] _itemsIds = Array.Empty<int>();
    public readonly int[] _positions = Array.Empty<int>();
    public readonly int[] _quantities = Array.Empty<int>();
    public readonly long[] _durabilities = Array.Empty<long>();
    public readonly int[] _qualities = Array.Empty<int>();
    public readonly bool[] _isAwakened = Array.Empty<bool>();
    public readonly string[] _crafterNames = Array.Empty<string>();

    public AssetOverviewUnfreezeCacheResponse(Dictionary<byte, object> parameters) : base(parameters)
    {
        Log.Verbose("Got {PacketType} packet.", GetType());

        try
        {
            if (parameters.TryGetValue(0, out object? containerId))
            {
                _containerId = containerId.ToGuid() ?? Guid.Empty;
            }
            if (parameters.TryGetValue(2, out object? itemIds))
            {
                _itemsIds = itemIds.ToIntArray();
            }
            if (parameters.TryGetValue(3, out object? positions))
            {
                _positions = positions.ToIntArray();
            }
            if (parameters.TryGetValue(4, out object? quantities))
            {
                _quantities = quantities.ToIntArray();
            }
            if (parameters.TryGetValue(5, out object? durabilities))
            {
                _durabilities = durabilities.ToLongArray();
            }
            if (parameters.TryGetValue(7, out object? qualities))
            {
                _qualities = qualities.ToIntArray();
            }
            if (parameters.TryGetValue(8, out object? crafterNames))
            {
                _crafterNames = crafterNames.ToStringArray();
            }
            if (parameters.TryGetValue(11, out object? isAwakened))
            {
                _isAwakened = isAwakened.ToBoolArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
