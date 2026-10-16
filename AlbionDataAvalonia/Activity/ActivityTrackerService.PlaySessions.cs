using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

/// <summary>
/// A play session opens when a character logs in and waits until its first run. It ends at its last
/// activity after the idle limit, New session, a character switch, recording turning off or the app
/// stopping; logging in again within the idle limit continues one the app stop closed.
/// </summary>
public sealed partial class ActivityTrackerService
{
    // The active play session; null while the open one waits for its first activity.
    private ActivityPlaySession? playSession;
    // The character whose play session is open or waiting.
    private ActivityOwner? playOwner;
    // When the open play session began in this app run. An instance run never starts before it.
    private DateTime playOpenedAt;
    // The last run of the active play session ended here.
    private DateTime? lastRunClosedAt;
    private bool playSessionDirty;
    private readonly Dictionary<Guid, ActivityPlaySession> deferredPlaySessions = new();

    public Guid? CurrentPlaySessionId { get { lock (sync) return playSession?.Id; } }

    /// <summary>Ends the play session at its last activity; the next activity starts a new one.</summary>
    public void StartNewSession(Guid? expectedPlaySessionId = null)
    {
        lock (sync)
        {
            if (expectedPlaySessionId is { } expected && playSession?.Id != expected) return;
            var now = DateTime.UtcNow;
            ResolvePending(now);
            ClosePlaySession(now, ActivityEndReason.Finished);
            resumable = null;
            loot.ResetCandidates();
            locations.ClearPending();
            ClearPendingActorMetadata();
            gathering.Reset();
            fameCollector.Reset();
            mobParticipation.Clear();
            pendingTerminals.Clear();
            mapEnteredAt = now;
            SaveHeaders();
        }
        _ = persistence.FlushAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A login, or the character switching, opens a waiting play session for that character.
    private void OpenPlaySessionFor(ActivityOwner? owner, DateTime at)
    {
        if (owner is null || owner == playOwner) return;
        if (playOwner is not null) ClosePlaySession(at, ActivityEndReason.CharacterChanged);
        playOwner = owner;
        playOpenedAt = at;
        if (persistence.TakeRecentPlaySession(owner, at) is { } recent)
        {
            playSession = recent with
            {
                Revision = recent.Revision + 1,
                State = ActivityState.Open,
                EndReason = ActivityEndReason.None,
                EndedAtUtc = null
            };
            lastRunClosedAt = recent.EndedAtUtc;
            playSessionDirty = true;
            headersDirty = true;
        }
    }

    // The first run of a waiting play session starts counting it.
    private ActivityPlaySession? ActivatePlaySession(DateTime runStartedAt, DateTime at)
    {
        if (playOwner is null) return null;
        if (playSession is null)
        {
            playSession = new ActivityPlaySession
            {
                Id = Guid.NewGuid(),
                Server = playOwner.Server,
                CharacterId = playOwner.CharacterId,
                CharacterName = playOwner.CharacterName,
                StartedAtUtc = runStartedAt,
                LastActivityAtUtc = at,
                State = ActivityState.Open
            };
            lastRunClosedAt = null;
        }
        playSessionDirty = true;
        headersDirty = true;
        return playSession;
    }

    private void MarkPlayActivity(DateTime at)
    {
        if (playSession is null || at <= playSession.LastActivityAtUtc) return;
        playSession = playSession with { LastActivityAtUtc = at, Revision = playSession.Revision + 1 };
        playSessionDirty = true;
        headersDirty = true;
    }

    // Closes the open run and the play session at the last activity plus a short grace.
    private void ClosePlaySession(DateTime at, ActivityEndReason reason)
    {
        var end = playSession is { } active && active.LastActivityAtUtc + ActivityPlaySessionProjection.ActivityGrace < at
            ? active.LastActivityAtUtc + ActivityPlaySessionProjection.ActivityGrace : at;
        if (session is not null) CloseSession(end > session.StartedAtUtc ? end : session.StartedAtUtc, reason);
        if (playSession is { } closing)
        {
            var ended = closing with
            {
                Revision = closing.Revision + 1,
                State = reason == ActivityEndReason.Interrupted ? ActivityState.Interrupted : ActivityState.Closed,
                EndReason = reason,
                EndedAtUtc = lastRunClosedAt is { } runEnd && runEnd > end ? runEnd : end
            };
            QueuePlaySession(ended);
        }
        playSession = null;
        playSessionDirty = false;
        lastRunClosedAt = null;
        playOpenedAt = at;
    }

    private void ExpirePlaySession(DateTime at)
    {
        if (playSession is { } active && at >= active.LastActivityAtUtc + ActivityPlaySessionProjection.IdleLimit)
            ClosePlaySession(at, ActivityEndReason.Inactivity);
    }

    private void SavePlaySession()
    {
        if (playSession is null || !playSessionDirty) return;
        QueuePlaySession(playSession);
        playSessionDirty = false;
    }

    private void QueuePlaySession(ActivityPlaySession value)
    {
        if (persistence.EnqueuePlaySession(value)) return;
        if (!deferredPlaySessions.TryGetValue(value.Id, out var old) || value.Revision > old.Revision) deferredPlaySessions[value.Id] = value;
        storageSuspended = true;
    }

    private void ResumeDeferredPlaySessions()
    {
        foreach (var header in deferredPlaySessions.Values.ToArray())
            if (persistence.EnqueuePlaySession(header)) deferredPlaySessions.Remove(header.Id);
    }
}
