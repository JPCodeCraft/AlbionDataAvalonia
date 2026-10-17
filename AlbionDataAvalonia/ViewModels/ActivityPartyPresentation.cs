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
    public string TimeText => UiFormat.LocalTime(AtUtc);
    public string Change => Joined ? "Joined" : "Left";
    public string Player => ActivityPartyText.Preview(Players);
    public string PlayerDetails => ActivityPartyText.Members(Players);
    public string PartySizeText => PartySize <= 0 ? "" : PartySize == 1 ? "Solo" : PartySize.ToString("N0");
}

public sealed record ActivityPartyMemberRow(string Name, string JoinedText, string LeftText, string DurationText, bool StayedUntilEnd);

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
                UiFormat.Duration(total.TotalSeconds), stayed));
        }).OrderBy(x => x.Order).ThenBy(x => x.Row.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.Row).ToList();
        foreach (var name in recordedMembers.Where(name => !members.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase))
            rows.Add(new(name, "Not recorded", "", "", false));
        if (rows.Count == 0) return Empty;
        var summary = $"{UiFormat.Count(rows.Count, "member")} besides you";
        if (peak > 1) summary += $" · peak party size {peak:N0}";
        if (changes.Count > 0) summary += $" · {UiFormat.Count(changes.Count, "change")}";
        if (events.Count == 0) summary += " · joins and departures were not recorded for this session";
        changes.Reverse();
        return new(rows, changes, summary);
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
