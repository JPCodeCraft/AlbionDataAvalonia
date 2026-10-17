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
    public string MetadataText => $"{UiFormat.LocalDateTime(Source.StartedAtUtc)} · {Source.CharacterName} · {ActivityDisplay.Server(Source.Server)}";
    public string DurationText => UiFormat.Duration(Durations(DateTime.UtcNow).PlaySeconds);
    public string RunsText => UiFormat.Count(Runs.Count, "run");
    public string TagsText => ActivityDisplay.Tags(summary.Tags);
    public string StateText => IsOpen ? "Active" : Source.State == ActivityState.Interrupted ? "App closed" : "";
    public bool IsImported => summary.IsLegacyAggregate;
    public bool IsPartial => Runs.Any(x => x.IsPartial);
    public bool HasParty => partyMembers.Count > 0;
    // Members are read again only after one of the runs changed.
    public bool NeedsParty => partyKey != PartyKey(Header) && !IsImported;
    public string PartyText => ActivityPartyText.With(partyMembers);
    public string PartyDetailsText => HasParty ? $"Party members in this session ({partyMembers.Count:N0})\n{ActivityPartyText.Members(partyMembers)}" : "";

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

    public void SetParty(ActivityPlaySessionHeader source, IReadOnlyList<string> members)
    {
        partyKey = PartyKey(source);
        if (partyMembers.SequenceEqual(members)) return;
        partyMembers = members;
        OnPropertyChanged(nameof(HasParty));
        OnPropertyChanged(nameof(PartyText));
        OnPropertyChanged(nameof(PartyDetailsText));
    }

    private static string PartyKey(ActivityPlaySessionHeader header) => string.Join(",", header.Runs.Select(run => $"{run.Id}:{run.Revision}"));
}
