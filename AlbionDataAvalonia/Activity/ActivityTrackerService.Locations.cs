using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Responses;
using System;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    private readonly ActivityLocationCollector locations = new();
    private ActivityMapMetadata mapMetadata = new();
    private ActivityEvent? locationObservation;

    internal void CancelEntranceSelection(RequestPacket packet)
    {
        lock (sync)
        {
            if (disposed || !HasConfirmedContext || Utc(packet.CapturedAt) < mapEnteredAt
                || !SameConnection(packet.ConnectionId, packet.MessageIdentity)) return;
            locations.ClearPending();
        }
    }

    private void ObserveLocationTransition(ChangeClusterResponse packet, DateTime at)
    {
        locations.Observe(packet, at);
        if (packet.ReturnCode != 0 || map is null || packet.Destination != map.RawLocationId) return;
        var enriched = mapMetadata.Merge(ActivityLocationCollector.FromCore(
            ActivityInstanceMetadata.FromTransition(map.Kind, packet.LayoutId, packet.DungeonInformation)));
        if (enriched == mapMetadata) return;
        if (enriched.Layout != mapMetadata.Layout) liveMapVersion++;
        mapMetadata = enriched;
        if (!CanRecord || session is null) return;
        for (var i = 0; i < visits.Count; i++)
            if (visits[i].RawMapId == map.RawLocationId)
                visits[i] = visits[i] with { Metadata = (visits[i].Metadata ?? new()).Merge(enriched) };
        session = session with { Revision = session.Revision + 1, VisitsJson = JsonSerializer.Serialize(visits) };
        headersDirty = true;
        if (locationObservation is { } original && original.SessionId == session.Id && original.RawMapId == map.RawLocationId)
        {
            var amended = original with { Revision = original.Revision + 1, DetailsJson = LocationDetails() };
            if (persistence.Amend(amended)) locationObservation = amended;
            else storageSuspended = true;
        }
    }

    private void RestoreLocationMetadata() => mapMetadata =
        (visits.LastOrDefault(x => x.RawMapId == map?.RawLocationId && x.Metadata is not null)?.Metadata ?? new()).Merge(mapMetadata);

    private string LocationDetails() => JsonSerializer.Serialize(new
    {
        OriginalName = map?.DisplayName,
        map?.ParentClusterId,
        map?.SourceClusterId,
        GroupingInferred = session?.HasInferredGrouping ?? false,
        MapMetadata = mapMetadata
    });

    private ActivityEvent LocationEvent(DateTime at, string? origin) => locationObservation =
        WithSource(Stamp(ActivityEventKind.Location, at, origin), actors.Local) with { DetailsJson = LocationDetails() };
}
