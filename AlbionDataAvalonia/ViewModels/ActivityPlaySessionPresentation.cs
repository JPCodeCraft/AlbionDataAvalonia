using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>One History row: a play session and its runs.</summary>
public sealed class ActivityPlaySessionRow : ObservableObject
{
    private ActivityScope scope;
    private ActivitySummary summary = new();
    private string? partyKey;
    private IReadOnlyList<string> partyMembers = [];
    private bool showsOwner;

    public ActivityPlaySessionRow(ActivityPlaySessionHeader header, ActivityScope scope)
    {
        Header = header;
        Update(header, scope);
    }

    public ActivityPlaySessionHeader Header { get; private set; }
    public ActivityPlaySession Source => Header.PlaySession;
    public IReadOnlyList<ActivitySession> Runs => Header.Runs;
    public Guid Id => Source.Id;
    public bool IsOpen => Source.State == ActivityState.Open;
    public ActivitySummary Summary => summary;
    public string Title => ActivityDisplay.PlaySessionTitle(Runs);
    public string OwnerText => $"{Source.CharacterName} · {ActivityDisplay.Server(Source.Server)}";
    // An open session reads Active; a closed one says how long ago it ended, with the dates in its tooltip.
    public string WhenText => IsOpen ? "Active" : UiFormat.TimeAgo(EndOf(Header), DateTime.UtcNow);
    public string WhenDetailsText => $"Started {UiFormat.LocalDateTime(StartOf(Header))}" +
        (IsOpen ? "" : $"\nEnded {UiFormat.LocalDateTime(EndOf(Header))}") +
        (Source.State == ActivityState.Interrupted ? " when the app closed" : "") + $"\n{OwnerText}";
    public string DurationText => UiFormat.Duration(Durations(DateTime.UtcNow).PlaySeconds);
    public string RunsText => UiFormat.Count(Runs.Count, "run");
    public bool HasSeveralRuns => Runs.Count > 1;
    public string TagsText => ActivityDisplay.Tags(summary.Tags);
    public string TotalSilverText => UiFormat.Compact(summary.Silver + summary.LootValue + summary.ResourceValue);
    public bool HasTotalSilver => summary.Silver + summary.LootValue + summary.ResourceValue > 0;
    public bool IsImported => summary.IsLegacyAggregate;
    public bool IsPartial => Runs.Any(x => x.IsPartial);
    public bool HasParty => partyMembers.Count > 0;
    public IReadOnlyList<string> PartyMembers => partyMembers;
    // Members are read again only after one of the runs changed.
    public bool NeedsParty => partyKey != PartyKey(Header) && !IsImported;
    public string PartyText => ActivityPartyText.With(partyMembers);
    public string PartyDetailsText => HasParty ? $"Party members in this session ({partyMembers.Count:N0})\n{ActivityPartyText.Members(partyMembers)}" : "";
    // The character and server show only on sessions played on another character or server than most.
    public string DetailsText => string.Join(" · ", new[] { showsOwner ? OwnerText : "", PartyText }.Where(text => text.Length > 0));
    public bool HasDetails => DetailsText.Length > 0;
    public bool HasFooter => HasDetails || summary.Tags != ActivityTags.None;

    public bool ShowsOwner
    {
        get => showsOwner;
        set
        {
            if (!SetProperty(ref showsOwner, value)) return;
            OnPropertyChanged(nameof(DetailsText));
            OnPropertyChanged(nameof(HasDetails));
            OnPropertyChanged(nameof(HasFooter));
        }
    }

    // A session started with Start session begins at the press, before its first run.
    public static DateTime StartOf(ActivityPlaySessionHeader header) =>
        header.Runs.Count == 0 || header.PlaySession.StartedAtUtc < header.Runs.Min(x => x.StartedAtUtc)
            ? header.PlaySession.StartedAtUtc : header.Runs.Min(x => x.StartedAtUtc);

    public static DateTime EndOf(ActivityPlaySessionHeader header) => header.PlaySession.EndedAtUtc ??
        (header.Runs.Count == 0 ? header.PlaySession.LastActivityAtUtc : header.Runs.Max(x => x.ClosedAtUtc ?? x.LastObservedAtUtc));

    public ActivityPlayDurations Durations(DateTime nowUtc, ActivitySession? liveRun = null) =>
        ActivityPlaySessionProjection.Durations(Source, Runs, liveRun?.PlaySessionId == Id ? liveRun : null, nowUtc);

    public void Update(ActivityPlaySessionHeader header, ActivityScope scope)
    {
        if (this.scope == scope && summary.TagVersion != 0 && Header.PlaySession == header.PlaySession &&
            Header.Runs.SequenceEqual(header.Runs)) return;
        Header = header;
        this.scope = scope;
        summary = ActivityPlaySessionProjection.Summary(header.Runs, scope);
        OnPropertyChanged(string.Empty);
    }

    public void RefreshDuration() => OnPropertyChanged(nameof(DurationText));
    public void RefreshWhen() => OnPropertyChanged(nameof(WhenText));

    public void SetParty(ActivityPlaySessionHeader source, IReadOnlyList<string> members)
    {
        partyKey = PartyKey(source);
        if (partyMembers.SequenceEqual(members)) return;
        partyMembers = members;
        OnPropertyChanged(nameof(HasParty));
        OnPropertyChanged(nameof(PartyMembers));
        OnPropertyChanged(nameof(PartyText));
        OnPropertyChanged(nameof(PartyDetailsText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(HasDetails));
        OnPropertyChanged(nameof(HasFooter));
    }

    private static string PartyKey(ActivityPlaySessionHeader header) => string.Join(",", header.Runs.Select(run => $"{run.Id}:{run.Revision}"));
}
