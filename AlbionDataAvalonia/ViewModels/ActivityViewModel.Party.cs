using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Party.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private PartyTrackerService? partyTracker;
    private LatestUiValueDispatcher<PartyTrackerSnapshot>? partySnapshots;

    [ObservableProperty] private string currentPartyText = "";
    [ObservableProperty] private string currentPartyDetailsText = "";

    public bool HasCurrentParty => !string.IsNullOrEmpty(CurrentPartyText);

    partial void OnCurrentPartyTextChanged(string value) => OnPropertyChanged(nameof(HasCurrentParty));

    private void AttachPartyTracker(PartyTrackerService? party)
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
        if (isDisposed) return;
        var local = snapshot.Members.FirstOrDefault(member => member.IsLocalPlayer);
        var others = snapshot.Members.Where(member => !member.IsLocalPlayer)
            .Select(member => ActivityDisplay.ActorName(member.Name)).ToArray();
        if (local is null && others.Length == 0)
        {
            CurrentPartyText = "";
            CurrentPartyDetailsText = "";
            return;
        }
        CurrentPartyText = others.Length == 0 ? "Solo" : $"Party: {ActivityDisplay.PartyNames(others)}";
        var lines = (local is null ? others : others.Prepend($"{ActivityDisplay.ActorName(local.Name)} (you)")).ToArray();
        CurrentPartyDetailsText = others.Length == 0 ? "Not in a party" : $"Party ({lines.Length})\n{string.Join("\n", lines)}";
    }

    // Party members are derived from the session's recorded events, so they stay accurate
    // for history without storing a roster on the session header.
    private async Task LoadSessionPartiesAsync(CancellationToken cancellationToken)
    {
        if (persistence is null) return;
        var pending = Sessions.Where(row => row.NeedsParty).Take(1000).ToArray();
        if (pending.Length == 0) return;
        var requested = pending.ToDictionary(row => row.Id, row => row.Source);
        var members = await persistence.GetSessionPartyMembersAsync(requested.Keys, cancellationToken);
        if (isDisposed) return;
        foreach (var row in Sessions)
            if (requested.TryGetValue(row.Id, out var source))
                row.SetParty(source, members.TryGetValue(row.Id, out var names) ? names : []);
    }

    private void DisposeParty()
    {
        if (partyTracker is not null) partyTracker.SnapshotChanged -= OnPartySnapshotChanged;
        partySnapshots?.Dispose();
    }
}
