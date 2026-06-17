using Albion.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Events;

public sealed class UpdateLootChestEvent : BaseEvent
{
    public long ObjectId { get; }
    public int State { get; }
    public IReadOnlyList<Guid> PlayerGuids { get; } = Array.Empty<Guid>();

    public UpdateLootChestEvent(Dictionary<byte, object> parameters) : base(parameters)
    {
        try
        {
            if (parameters.TryGetValue(0, out var objectId))
            {
                ObjectId = objectId.ToLong();
            }

            if (parameters.TryGetValue(1, out var state))
            {
                State = state.ToInt();
            }

            var playerGuids = new List<Guid>();
            if (parameters.TryGetValue(3, out var firstPlayerGuids))
            {
                playerGuids.AddRange(firstPlayerGuids.ToGuidArray());
            }

            if (parameters.TryGetValue(5, out var secondPlayerGuids))
            {
                playerGuids.AddRange(secondPlayerGuids.ToGuidArray());
            }

            if (playerGuids.Count > 0)
            {
                PlayerGuids = playerGuids
                    .Where(guid => guid != Guid.Empty)
                    .Distinct()
                    .ToArray();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }
}
