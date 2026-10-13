using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public sealed record ActivityStorageSummary(long Sessions, long Encounters, long Events, long ItemPickups,
    long ActivityBytes, long DatabaseBytes, bool IsActivitySizeEstimated);

public sealed record ActivityCleanupRequest(DateTime? OlderThanUtc, bool RemoveOutsidePartyLoot, Guid? ProtectedSessionId,
    DateTime? ObservedThroughUtc = null);

public sealed record ActivityCleanupPreview(ActivityCleanupRequest Request, long Sessions, long Encounters,
    long Events, long ItemPickups)
{
    public bool HasData => Sessions > 0 || Events > 0;
}

public sealed record ActivityCleanupResult(ActivityCleanupPreview Deleted, IReadOnlyList<Guid> DeletedSessionIds);
