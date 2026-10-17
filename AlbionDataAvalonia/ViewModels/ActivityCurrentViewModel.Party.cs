using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Party.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityCurrentViewModel
{
    private const int MaximumPartyChanges = 12;
    private PartyTrackerService? partyTracker;
    private LatestUiValueDispatcher<PartyTrackerSnapshot>? partySnapshots;
    private Dictionary<Guid, string>? shownPartyMembers;
    private Guid? shownPartyLocalGuid;

    [ObservableProperty] private string currentPartyText = "";
    [ObservableProperty] private string currentPartyPreviewText = "";
    [ObservableProperty] private string currentPartyHeaderText = "";
    [ObservableProperty] private string currentPartyDetailsText = "";
    [ObservableProperty] private ObservableCollection<ActivityPartyMemberChip> currentPartyMembers = new();

    public ObservableCollection<ActivityPartyChangeRow> CurrentPartyChanges { get; } = new();
    public bool HasCurrentParty => !string.IsNullOrEmpty(CurrentPartyText);
    public bool HasCurrentPartyPreview => !string.IsNullOrEmpty(CurrentPartyPreviewText);
    public bool HasCurrentPartyChanges => CurrentPartyChanges.Count > 0;

    partial void OnCurrentPartyTextChanged(string value) => OnPropertyChanged(nameof(HasCurrentParty));
    partial void OnCurrentPartyPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasCurrentPartyPreview));

    internal void AttachPartyTracker(PartyTrackerService? party)
    {
        if (party is null) return;
        partyTracker = party;
        partySnapshots = new(ApplyPartySnapshot);
        party.SnapshotChanged += OnPartySnapshotChanged;
        ApplyPartySnapshot(party.CurrentSnapshot);
    }

    // Roster events arrive on the capture thread; only the latest snapshot reaches the UI.
    private void OnPartySnapshotChanged(PartyTrackerSnapshot snapshot) => partySnapshots?.Post(snapshot);

    private void ApplyPartySnapshot(PartyTrackerSnapshot snapshot)
    {
        if (page.IsDisposed) return;
        var local = snapshot.Members.FirstOrDefault(member => member.IsLocalPlayer);
        var others = snapshot.Members.Where(member => !member.IsLocalPlayer && member.UserGuid is not null)
            .GroupBy(member => member.UserGuid!.Value).ToDictionary(group => group.Key, group => ActivityDisplay.ActorName(group.First().Name));
        RecordPartyChanges(local?.UserGuid, others);
        var names = others.Values.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (local is null && names.Length == 0)
        {
            CurrentPartyText = CurrentPartyPreviewText = CurrentPartyHeaderText = CurrentPartyDetailsText = "";
            CurrentPartyMembers = new();
            return;
        }
        var size = names.Length + (local is null ? 0 : 1);
        CurrentPartyText = names.Length == 0 ? "Solo" : $"Party · {size:N0}";
        CurrentPartyPreviewText = ActivityPartyText.Preview(names);
        CurrentPartyHeaderText = names.Length == 0 ? "Not in a party" : $"Party · {size:N0} members";
        CurrentPartyDetailsText = names.Length == 0 ? "Not in a party" : $"Party · {size:N0} members\n{ActivityPartyText.Members(names)}\nClick for details";
        var chips = new List<ActivityPartyMemberChip>(size);
        if (local is not null) chips.Add(new($"{ActivityDisplay.ActorName(local.Name)} (you)", true));
        chips.AddRange(names.Select(name => new ActivityPartyMemberChip(name, false)));
        if (!CurrentPartyMembers.SequenceEqual(chips)) CurrentPartyMembers = new(chips);
    }

    // The first snapshot and character switches set a baseline; later differences are changes.
    private void RecordPartyChanges(Guid? localGuid, Dictionary<Guid, string> others)
    {
        var named = others.Where(x => x.Value != "Unknown").ToDictionary(x => x.Key, x => x.Value);
        if (shownPartyMembers is null || shownPartyLocalGuid != localGuid)
        {
            shownPartyMembers = named;
            shownPartyLocalGuid = localGuid;
            return;
        }
        var now = DateTime.UtcNow;
        var size = (localGuid is null ? 0 : 1) + others.Count;
        AddPartyChange(now, false, shownPartyMembers.Where(x => !named.ContainsKey(x.Key)).Select(x => x.Value), size);
        AddPartyChange(now, true, named.Where(x => !shownPartyMembers.ContainsKey(x.Key)).Select(x => x.Value), size);
        while (CurrentPartyChanges.Count > MaximumPartyChanges) CurrentPartyChanges.RemoveAt(CurrentPartyChanges.Count - 1);
        shownPartyMembers = named;
        OnPropertyChanged(nameof(HasCurrentPartyChanges));
    }

    // A large party arrives as a burst of roster updates; nearby changes of one kind share a row.
    private void AddPartyChange(DateTime at, bool joined, IEnumerable<string> players, int size)
    {
        var names = players.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0) return;
        if (CurrentPartyChanges.Count > 0 && CurrentPartyChanges[0] is { } latest && latest.Joined == joined &&
            at - latest.AtUtc <= ActivityPartyChangeRow.MergeWindow)
            CurrentPartyChanges[0] = latest with { Players = latest.Players.Concat(names).ToArray(), PartySize = size };
        else CurrentPartyChanges.Insert(0, new(at, joined, names, size));
    }

    private void DisposeParty()
    {
        if (partyTracker is not null) partyTracker.SnapshotChanged -= OnPartySnapshotChanged;
        partySnapshots?.Dispose();
    }
}
