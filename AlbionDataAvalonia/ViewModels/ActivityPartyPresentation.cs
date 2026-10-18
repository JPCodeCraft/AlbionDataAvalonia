using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels;

public sealed record ActivityPartyMemberChip(string Name, bool IsYou);

public sealed record ActivityPartyChangeRow(DateTime AtUtc, bool Joined, IReadOnlyList<string> Players, int PartySize)
{
    internal static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(10);
    // Changes from several sessions can fall on different days.
    public bool WithDate { get; init; }
    public string TimeText => WithDate ? UiFormat.LocalDateTime(AtUtc) : UiFormat.LocalTime(AtUtc);
    public string Change => Joined ? "Joined" : "Left";
    public string Player => ActivityPartyText.Preview(Players);
    public string PlayerDetails => ActivityPartyText.Members(Players);
    public string PartySizeText => PartySize <= 0 ? "" : PartySize == 1 ? "Solo" : PartySize.ToString("N0");
}

public sealed record ActivityPartyMemberRow(string Name, string JoinedText, string LeftText, string DurationText, bool StayedUntilEnd)
{
    // The times behind the texts, so several sessions can add up; members known only from activity have none.
    public TimeSpan? Duration { get; init; }
    public DateTime? FirstSeenUtc { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public bool StillInParty { get; init; }
}

public sealed record ActivityPartyHistory(IReadOnlyList<ActivityPartyMemberRow> Members, IReadOnlyList<ActivityPartyChangeRow> Changes, string SummaryText)
{
    public static readonly ActivityPartyHistory Empty = new([], [], "");
    public int MemberCount => Members.Count;
    public int ChangeCount => Changes.Count;

    // Membership intervals come from the journaled roster and join/leave events. Older sessions
    // only know members from their recorded activity, without times.
    public static ActivityPartyHistory Build(IReadOnlyList<ActivityEvent> events, ActivitySession session,
        IReadOnlyList<string> recordedMembers, DateTime nowUtc)
    {
        var end = session.State == ActivityState.Open ? nowUtc : session.ClosedAtUtc ?? session.LastObservedAtUtc;
        var members = new Dictionary<string, MemberSpan>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<ActivityPartyChangeRow>();
        var peak = 0;
        foreach (var value in events.Where(x => ActivityClassification.IsParty(x.Kind)).OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.CaptureOrder))
        {
            var name = ActivityDisplay.ActorName(value.SourceName);
            var details = ActivityPartyDetails.Parse(value.DetailsJson);
            peak = Math.Max(peak, details?.PartySize ?? 0);
            if (!members.TryGetValue(name, out var span)) members[name] = span = new MemberSpan();
            if (value.Kind == ActivityEventKind.PartyJoined)
            {
                if (span.FirstJoined is null) { span.FirstJoined = value.CapturedAtUtc; span.AtStart = details?.Roster == true; }
                span.OpenedAt ??= value.CapturedAtUtc;
            }
            else
            {
                // A departure without a recorded join means the member was present from the start.
                var opened = span.OpenedAt ?? (span.FirstJoined is null ? session.StartedAtUtc : null);
                if (span.FirstJoined is null) { span.FirstJoined = session.StartedAtUtc; span.AtStart = true; }
                if (opened is { } start) span.Total += Positive(value.CapturedAtUtc - start);
                span.OpenedAt = null;
                span.LastLeft = value.CapturedAtUtc;
            }
            if (details?.Roster == true) continue;
            // Members joining or leaving together share one row, as they share one chart marker.
            var joined = value.Kind == ActivityEventKind.PartyJoined;
            if (changes.Count > 0 && changes[^1] is { } latest && latest.Joined == joined &&
                value.CapturedAtUtc - latest.AtUtc <= ActivityPartyChangeRow.MergeWindow)
                changes[^1] = latest with { Players = latest.Players.Append(name).ToArray(), PartySize = details?.PartySize ?? latest.PartySize };
            else changes.Add(new(value.CapturedAtUtc, joined, [name], details?.PartySize ?? 0));
        }
        var rows = members.Select(x =>
        {
            var span = x.Value;
            var total = span.Total + (span.OpenedAt is { } open ? Positive(end - open) : TimeSpan.Zero);
            var stayed = span.OpenedAt is not null;
            return (Order: span.FirstJoined ?? DateTime.MaxValue, Row: new ActivityPartyMemberRow(x.Key,
                span.AtStart ? "Session start" : span.FirstJoined is { } joined ? UiFormat.LocalTime(joined) : "",
                stayed ? session.State == ActivityState.Open ? "Still in party" : "Session end" : span.LastLeft is { } left ? UiFormat.LocalTime(left) : "",
                UiFormat.Duration(total.TotalSeconds), stayed)
            {
                Duration = total,
                FirstSeenUtc = span.FirstJoined,
                LastSeenUtc = stayed ? end : span.LastLeft,
                StillInParty = stayed && session.State == ActivityState.Open
            });
        }).OrderBy(x => x.Order).ThenBy(x => x.Row.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Row).ToList();
        foreach (var name in recordedMembers.Where(name => !members.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase))
            rows.Add(new(name, UiText.NotRecorded, "", "", false));
        if (rows.Count == 0) return Empty;
        var summary = $"{UiFormat.Count(rows.Count, "member")} besides you";
        if (peak > 1) summary += $" · peak party size {peak:N0}";
        if (changes.Count > 0) summary += $" · {UiFormat.Count(changes.Count, "change")}";
        if (events.Count == 0) summary += " · joins and departures were not recorded for this session";
        changes.Reverse();
        return new(rows, changes, summary);
    }

    /// <summary>
    /// Several sessions list each member once, with their time in party added up. Joins, departures and
    /// changes show their dates, since the sessions can be days apart.
    /// </summary>
    public static ActivityPartyHistory Combine(IReadOnlyList<ActivityPartyHistory> sessions)
    {
        var members = sessions.SelectMany(x => x.Members).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var first = group.Where(x => x.FirstSeenUtc is not null).MinBy(x => x.FirstSeenUtc);
            var last = group.Where(x => x.LastSeenUtc is not null).MaxBy(x => x.LastSeenUtc);
            var timed = group.Where(x => x.Duration is not null).ToArray();
            var total = TimeSpan.FromSeconds(timed.Sum(x => x.Duration!.Value.TotalSeconds));
            return new ActivityPartyMemberRow(group.First().Name,
                first?.FirstSeenUtc is { } joined ? UiFormat.LocalDateTime(joined) : UiText.NotRecorded,
                last is { StillInParty: true } ? "Still in party" : last?.LastSeenUtc is { } left ? UiFormat.LocalDateTime(left) : "",
                timed.Length > 0 ? UiFormat.Duration(total.TotalSeconds) : "", last?.StayedUntilEnd == true)
            {
                Duration = timed.Length > 0 ? total : null,
                FirstSeenUtc = first?.FirstSeenUtc,
                LastSeenUtc = last?.LastSeenUtc,
                StillInParty = last?.StillInParty == true
            };
        }).OrderBy(x => x.FirstSeenUtc ?? DateTime.MaxValue).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (members.Length == 0) return Empty;
        var changes = sessions.SelectMany(x => x.Changes).OrderByDescending(x => x.AtUtc).Select(x => x with { WithDate = true }).ToArray();
        var summary = $"{UiFormat.Count(members.Length, "member")} besides you across {UiFormat.Count(sessions.Count, "session")}";
        if (changes.Length > 0) summary += $" · {UiFormat.Count(changes.Length, "change")}";
        return new(members, changes, summary);
    }

    private static TimeSpan Positive(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;

    private sealed class MemberSpan
    {
        public DateTime? FirstJoined;
        public DateTime? OpenedAt;
        public DateTime? LastLeft;
        public TimeSpan Total;
        public bool AtStart;
    }
}

internal static class ActivityPartyText
{
    // Large parties summarize instead of listing every member inline; tooltips list them all.
    public static string With(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => $"With {names[0]}",
        2 => $"With {names[0]} and {names[1]}",
        _ => $"With {names[0]}, {names[1]} and {names.Count - 2:N0} {(names.Count == 3 ? "other" : "others")}"
    };

    public static string Preview(IReadOnlyList<string> names) => names.Count <= 2
        ? string.Join(", ", names) : $"{names[0]}, {names[1]} +{names.Count - 2:N0}";

    public static string Members(IReadOnlyList<string> names, int limit = 20) => string.Join("\n", names.Take(limit)) +
        (names.Count > limit ? $"\n+{names.Count - limit:N0} more" : "");
}
