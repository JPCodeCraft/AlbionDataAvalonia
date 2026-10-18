using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Responses;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    private readonly ActivityLocationCollector locations = new();
    private ActivityMapMetadata mapMetadata = new();
    private ActivityEvent? locationObservation;
    // Mists wisp spawns and border exits. Those of an instance being entered arrive just before its join, on the
    // new connection, so only landmarks on the current map's connection from shortly before its join place it.
    private static readonly TimeSpan LandmarkLead = TimeSpan.FromSeconds(10);
    private readonly List<(ActivityWorldPosition Position, DateTime At, string ConnectionId, PhotonMessageIdentity? Identity)> mistsLandmarks = new();

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
        ApplyMapMetadata(mapMetadata.Merge(ActivityLocationCollector.FromCore(
            ActivityInstanceMetadata.FromTransition(map.Kind, packet.LayoutId, packet.DungeonInformation))));
    }

    public void ObserveMistsLandmark(MistsLandmarkEvent packet)
    {
        if (packet.Position is not { } position) return;
        lock (sync)
        {
            if (disposed) return;
            if (mistsLandmarks.Count >= 256) mistsLandmarks.RemoveAt(0);
            mistsLandmarks.Add((position, Utc(packet.CapturedAt), packet.ConnectionId, packet.MessageIdentity));
            ApplyMapMetadata(WithMistsLayout(mapMetadata));
        }
    }

    // Entering the Mists through a wisp names no layout, so its landmarks place it.
    private ActivityMapMetadata WithMistsLayout(ActivityMapMetadata metadata)
    {
        if (map is not { Kind: ActivityMapKind.Mists } || metadata.LayoutTurns is not null) return metadata;
        var landmarks = mistsLandmarks.Where(x => x.At >= mapEnteredAt - LandmarkLead && SameConnection(x.ConnectionId, x.Identity))
            .Select(x => x.Position).ToArray();
        if (MistsLayoutRecognizer.Recognize(landmarks) is not { } placement) return metadata;
        Log.Debug("Placed {RawMapId} as Mists layout {Layout} turned {QuarterTurns} quarter turns from {Count} landmarks",
            map.RawLocationId, placement.Layout, placement.QuarterTurns, landmarks.Length);
        return metadata with { Layout = placement.Layout, LayoutTurns = placement.QuarterTurns };
    }

    private void ApplyMapMetadata(ActivityMapMetadata enriched)
    {
        if (map is null || enriched == mapMetadata) return;
        if (enriched.Layout != mapMetadata.Layout || enriched.LayoutTurns != mapMetadata.LayoutTurns) liveMapVersion++;
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
