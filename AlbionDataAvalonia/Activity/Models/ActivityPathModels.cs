using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public readonly record struct ActivityPathPoint(DateTime CapturedAtUtc, double X, double Y);

// A stretch of the local character's path on one map during a run, about a minute long.
// Points are packed to keep a long session small: per point, the milliseconds since
// StartedAtUtc (int32), then X and Y (float32), all little-endian.
public sealed record ActivityPathSegment
{
    public const int BytesPerPoint = 12;

    public long Id { get; init; }
    public Guid SessionId { get; init; }
    public string RawMapId { get; init; } = "";
    public DateTime StartedAtUtc { get; init; }
    public DateTime EndedAtUtc { get; init; }
    public int PointCount { get; init; }
    public byte[] Points { get; init; } = [];

    public static ActivityPathSegment Create(Guid sessionId, string rawMapId, IReadOnlyList<ActivityPathPoint> points)
    {
        if (points.Count == 0) throw new ArgumentException("A path segment needs at least one point.", nameof(points));
        var startedAt = points[0].CapturedAtUtc;
        var packed = new byte[points.Count * BytesPerPoint];
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var span = packed.AsSpan(index * BytesPerPoint, BytesPerPoint);
            BinaryPrimitives.WriteInt32LittleEndian(span, (int)Math.Clamp((point.CapturedAtUtc - startedAt).TotalMilliseconds, 0, int.MaxValue));
            BinaryPrimitives.WriteSingleLittleEndian(span[4..], (float)point.X);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..], (float)point.Y);
        }

        return new()
        {
            SessionId = sessionId,
            RawMapId = rawMapId,
            StartedAtUtc = startedAt,
            EndedAtUtc = points[^1].CapturedAtUtc,
            PointCount = points.Count,
            Points = packed
        };
    }

    public IReadOnlyList<ActivityPathPoint> ReadPoints()
    {
        var count = Math.Min(PointCount, Points.Length / BytesPerPoint);
        var points = new ActivityPathPoint[count];
        for (var index = 0; index < count; index++)
        {
            var span = Points.AsSpan(index * BytesPerPoint, BytesPerPoint);
            points[index] = new(StartedAtUtc.AddMilliseconds(BinaryPrimitives.ReadInt32LittleEndian(span)),
                BinaryPrimitives.ReadSingleLittleEndian(span[4..]), BinaryPrimitives.ReadSingleLittleEndian(span[8..]));
        }

        return points;
    }
}
