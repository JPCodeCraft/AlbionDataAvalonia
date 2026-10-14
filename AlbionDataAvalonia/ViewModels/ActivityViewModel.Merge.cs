namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    public string SessionActionText => HasCurrentSession ? "Merge session…" : "Resume session…";

    partial void OnHasCurrentSessionChanged(bool value) => OnPropertyChanged(nameof(SessionActionText));

    public ActivitySessionMergeViewModel? CreateSessionMerge()
    {
        if (isDisposed || tracker is null) return null;
        var session = tracker.GetCurrentContext().Session;
        return new ActivitySessionMergeViewModel(session,
            token => session is null ? tracker.GetResumeCandidatesAsync(token) : tracker.GetMergeCandidatesAsync(session.Id, token),
            async sourceId =>
            {
                if (session is null) await tracker.ResumeSessionAsync(sourceId);
                else await tracker.MergeSessionAsync(sourceId, session.Id);
                if (isDisposed) return;
                // Reload whole-session views after IDs and encounter numbers change.
                if (SelectedSession?.Id == sourceId) SelectedSession = null;
                loadedHistoryQuery = null;
                cachedDetailQuery = null;
                ++historyChangeVersion;
                ++detailChangeVersion;
                ++detailDataVersion;
                ++itemChangeVersion;
                ++currentCombatChangeVersion;
                currentPeriodKey = null;
                currentPeriodSnapshot = null;
                ClearCurrentLoot();
                cleanupReadVersion = -1;
                UpdateStatus();
                await RunSafelyAsync(RefreshVisibleAsync);
            });
    }
}
