using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public sealed partial class ActivitySessionMergeViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ActivitySession>>> loadCandidates;
    private readonly Func<Guid, Task> mergeSession;
    private readonly CancellationTokenSource cancellation = new();
    private bool disposed;

    public ActivitySessionMergeViewModel(ActivitySession? current,
        Func<CancellationToken, Task<IReadOnlyList<ActivitySession>>> loadCandidates, Func<Guid, Task> mergeSession)
    {
        this.loadCandidates = loadCandidates;
        this.mergeSession = mergeSession;
        IsResuming = current is null;
        CurrentTitle = current is null ? "" : ActivityDisplay.SessionTitle(current);
        CharacterText = current is null ? "" : $"{current.CharacterName} · {ActivityDisplay.Server(current.Server)}";
    }

    public bool IsResuming { get; }
    public string WindowTitle => IsResuming ? "Resume session" : "Merge session";
    public string Heading => IsResuming ? "Resume a previous session" : "Merge into current session";
    public string Description => IsResuming
        ? "Choose a session to continue. New activity will be added to it, excluding time since it ended."
        : "Choose a previous session. Its activity will be added to the current session, keeping separate encounters and excluding time between sessions.";
    public string EmptyText => IsResuming ? "No sessions available to resume." : "No earlier sessions for this character to merge.";
    public string CurrentTitle { get; }
    public string CharacterText { get; }
    public ObservableCollection<ActivitySessionRow> Candidates { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    private ActivitySessionRow? selectedSession;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool isLoading = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(MergeButtonText))]
    private bool isMerging;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private string errorText = "";

    public bool CanMerge => !disposed && !IsBusy && SelectedSession is not null;
    public bool IsBusy => IsLoading || IsMerging;
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public bool ShowEmpty => !IsLoading && !HasError && Candidates.Count == 0;
    public string MergeButtonText => IsResuming
        ? IsMerging ? "Resuming…" : "Resume session"
        : IsMerging ? "Merging…" : "Merge into current session";

    public async Task LoadAsync()
    {
        if (disposed) return;
        IsLoading = true;
        ErrorText = "";
        try
        {
            var candidates = await loadCandidates(cancellation.Token);
            if (disposed) return;
            Candidates.Clear();
            foreach (var session in candidates) Candidates.Add(new(session, ActivityScope.AllObserved));
            OnPropertyChanged(nameof(ShowEmpty));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Warning(error, "Could not load sessions for {SessionAction}.", IsResuming ? "resuming" : "merging");
            if (!disposed) ErrorText = "Could not load recent sessions. Close this window and try again.";
        }
        finally { if (!disposed) IsLoading = false; }
    }

    public async Task<bool> MergeAsync()
    {
        if (!CanMerge || SelectedSession is not { } selected) return false;
        IsMerging = true;
        ErrorText = "";
        try
        {
            await mergeSession(selected.Id);
            return true;
        }
        catch (InvalidOperationException error)
        {
            Log.Warning(error, "Activity session could not be {SessionAction}.", IsResuming ? "resumed" : "merged");
            ErrorText = IsResuming ? error.Message : "These sessions can no longer be merged. Close this window and select a session again.";
            if (!IsResuming)
            {
                SelectedSession = null;
                Candidates.Clear();
            }
            return false;
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not {SessionAction} Activity sessions.", IsResuming ? "resume" : "merge");
            ErrorText = IsResuming ? "The session could not be resumed. Please try again." : "The sessions could not be merged. Please try again.";
            return false;
        }
        finally { IsMerging = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
