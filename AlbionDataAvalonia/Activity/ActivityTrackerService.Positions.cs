using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Requests;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity;

// The client reports the local character's position while walking. The latest one is stamped
// on events that happen somewhere, and runs keep a sampled path of where the character went.
public sealed partial class ActivityTrackerService
{
    private static readonly TimeSpan PathSampleInterval = TimeSpan.FromSeconds(2);
    private const double PathSampleDistance = 3;
    private static readonly TimeSpan PathSegmentLength = TimeSpan.FromMinutes(1);
    private const int MaxPathSegmentPoints = 64;
    // The live map keeps a finer trail in memory than the stored path, for the current map only.
    private static readonly TimeSpan LiveTrailInterval = TimeSpan.FromMilliseconds(500);
    private const double LiveTrailDistance = 1;
    private static readonly TimeSpan LiveTrailAge = TimeSpan.FromHours(2);
    private const int MaxLiveTrailPoints = 8000;
    private const int MaxLiveMarkers = 600;

    private readonly List<ActivityPathPoint> pathPoints = new();
    private ActivityWorldPosition? position;
    private long positionEpoch = -1;
    private Guid? pathSessionId;
    private string pathMapId = "";
    private ActivityPathPoint? lastPathPoint;
    private readonly List<ActivityPathPoint> liveTrail = new();
    private readonly List<ActivityMapMarker> liveMarkers = new();
    private double? heading;
    private long liveMapVersion;
    private long ignoredMoveEpoch = -1;
    private long moveIdentityEpoch = -1;

    /// <summary>Changes whenever the live map has something new; cheap to poll.</summary>
    public long LiveMapVersion { get { lock (sync) return liveMapVersion; } }

    public ActivityLiveMap GetLiveMap()
    {
        lock (sync)
        {
            var current = positionEpoch == mapEpoch && liveTrail.Count > 0 ? liveTrail[^1] : (ActivityPathPoint?)null;
            return new(liveMapVersion, map?.RawLocationId ?? "", map?.DisplayName ?? "", current, heading,
                liveTrail.ToArray(), liveMarkers.ToArray());
        }
    }

    // Movement arrives several times a second, so this only updates state and never refreshes the UI.
    public void ObserveMove(MoveRequest packet)
    {
        if (packet.Position is not { } point) return;
        lock (sync)
        {
            if (disposed) return;
            var at = Utc(packet.CapturedAt);
            if (MoveIgnoredReason(packet, at) is { } reason)
            {
                NoteIgnoredMove(reason);
                return;
            }
            // Other requests still compare Photon header fields; say when Move would have failed that.
            if (moveIdentityEpoch != mapEpoch && !SameConnection(packet.ConnectionId, packet.MessageIdentity))
            {
                moveIdentityEpoch = mapEpoch;
                DiagnosticNote?.Invoke("Position accepted on the joined connection, though its Photon header differs from the join's: " +
                    $"challenge {packet.MessageIdentity?.Challenge} vs {connectionIdentity?.Challenge}, " +
                    $"stream {packet.MessageIdentity?.DirectionalConnection} vs {connectionIdentity?.DirectionalConnection}, " +
                    $"same parser {packet.MessageIdentity?.ReceiverGeneration == connectionIdentity?.ReceiverGeneration}");
            }
            PlacePosition(at, point);
        }
    }

    // Only the local client sends Move, so the joined socket is enough to tell this client's
    // moves apart. Photon header fields differ by direction and are not compared.
    private string? MoveIgnoredReason(MoveRequest packet, DateTime at) =>
        captureSuspended ? "capture is paused"
        : awaitingContext || !actors.HasIdentity ? "the character hasn't joined a map yet"
        : map is null ? "no map yet"
        : at < mapEnteredAt ? $"captured at {at:HH:mm:ss.fff} UTC, before the map was entered at {mapEnteredAt:HH:mm:ss.fff} UTC"
        : !string.IsNullOrEmpty(connectionId) && !string.IsNullOrEmpty(packet.ConnectionId) && connectionId != packet.ConnectionId
            ? $"it came over {packet.ConnectionId}, not the joined connection {connectionId}"
        : null;

    // One note per map is enough to explain a missing position.
    private void NoteIgnoredMove(string reason)
    {
        if (ignoredMoveEpoch == mapEpoch) return;
        ignoredMoveEpoch = mapEpoch;
        DiagnosticNote?.Invoke($"Position ignored: {reason}");
    }

    private void PlacePosition(DateTime at, ActivityWorldPosition point)
    {
        if (position is { } previous && positionEpoch == mapEpoch
            && Math.Abs(point.X - previous.X) + Math.Abs(point.Y - previous.Y) > 0.3)
            heading = Math.Atan2(point.Y - previous.Y, point.X - previous.X);
        position = point;
        positionEpoch = mapEpoch;
        TrackLive(at, point);
        if (!CanRecord || session is null) return;
        // The map entry is recorded before the first step on the new map; give it that first position.
        if (locationObservation is { PositionX: null } entry && entry.SessionId == session.Id && entry.MapEpoch == mapEpoch)
        {
            var placed = entry with { Revision = entry.Revision + 1, PositionX = point.X, PositionY = point.Y };
            if (persistence.Amend(placed)) locationObservation = placed;
        }
        SamplePath(session.Id, at, point);
    }

    private void TrackLive(DateTime at, ActivityWorldPosition point)
    {
        if (liveTrail.Count > 0)
        {
            var last = liveTrail[^1];
            if (at - last.CapturedAtUtc < LiveTrailInterval
                || Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2)) < LiveTrailDistance)
            {
                // The arrow still follows small steps; only the trail skips them.
                liveTrail[^1] = last with { X = point.X, Y = point.Y };
                liveMapVersion++;
                return;
            }
        }

        liveTrail.Add(new ActivityPathPoint(at, point.X, point.Y));
        var expired = 0;
        while (expired < liveTrail.Count && (liveTrail.Count - expired > MaxLiveTrailPoints || at - liveTrail[expired].CapturedAtUtc > LiveTrailAge)) expired++;
        if (expired > 0) liveTrail.RemoveRange(0, expired);
        liveMapVersion++;
    }

    // Called for every saved or amended event; placed events of the character and its party on this map are
    // kept. An amended pickup (now confirmed, priced or named) replaces its marker, or drops it when it no longer counts.
    private void TrackLiveMarker(ActivityEvent value)
    {
        if (value.MapEpoch != mapEpoch) return;
        var marker = ActivityMapMarker.From(value);
        var index = liveMarkers.FindIndex(x => x.EventId == value.Id);
        if (index >= 0)
        {
            if (marker is null) liveMarkers.RemoveAt(index);
            else liveMarkers[index] = marker;
            liveMapVersion++;
            return;
        }
        if (marker is null) return;
        liveMarkers.Add(marker);
        if (liveMarkers.Count > MaxLiveMarkers) liveMarkers.RemoveRange(0, liveMarkers.Count - MaxLiveMarkers);
        liveMapVersion++;
    }

    private void RemoveLiveMarker(Guid eventId)
    {
        if (liveMarkers.RemoveAll(x => x.EventId == eventId) > 0) liveMapVersion++;
    }

    private double? PositionXForStamp => positionEpoch == mapEpoch ? position?.X : null;
    private double? PositionYForStamp => positionEpoch == mapEpoch ? position?.Y : null;

    private static bool IsPlaced(ActivityEventKind kind) => kind is ActivityEventKind.Location or ActivityEventKind.MobKilled
        or ActivityEventKind.PlayerKilled or ActivityEventKind.Death or ActivityEventKind.Knockdown or ActivityEventKind.Loot
        or ActivityEventKind.Gathered or ActivityEventKind.FishingReward;

    private void SamplePath(Guid sessionId, DateTime at, ActivityWorldPosition point)
    {
        var mapId = map?.RawLocationId ?? "";
        if (pathSessionId != sessionId || pathMapId != mapId)
        {
            FlushPath();
            pathSessionId = sessionId;
            pathMapId = mapId;
            lastPathPoint = null;
        }

        if (lastPathPoint is { } last && (at - last.CapturedAtUtc < PathSampleInterval
            || Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2)) < PathSampleDistance)) return;
        var sample = new ActivityPathPoint(at, point.X, point.Y);
        pathPoints.Add(sample);
        lastPathPoint = sample;
        if (pathPoints.Count >= MaxPathSegmentPoints) FlushPath();
    }

    private void FlushStalePath(DateTime now)
    {
        if (pathPoints.Count > 0 && now - pathPoints[0].CapturedAtUtc >= PathSegmentLength) FlushPath();
    }

    // A full write queue drops the segment; the path is a nice-to-have and must not pause recording.
    private void FlushPath()
    {
        if (pathPoints.Count > 0 && pathSessionId is { } sessionId)
            persistence.EnqueuePath(ActivityPathSegment.Create(sessionId, pathMapId, pathPoints));
        pathPoints.Clear();
    }

    // Positions belong to one map; a new map or character starts without one.
    private void ForgetPosition()
    {
        FlushPath();
        position = null;
        positionEpoch = -1;
        pathSessionId = null;
        pathMapId = "";
        lastPathPoint = null;
        heading = null;
        liveTrail.Clear();
        liveMarkers.Clear();
        liveMapVersion++;
    }
}
