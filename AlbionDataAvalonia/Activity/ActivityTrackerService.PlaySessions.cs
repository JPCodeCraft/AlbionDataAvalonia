using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

/// <summary>
/// A play session starts at Start session or at the first activity, and its time runs on the clock until
/// End session, a character switch, recording turning off, or the idle limit without moving or acting,
/// which ends it at the last of those. Leaving the game or closing the app interrupts it; the same
/// character back within the idle limit continues it, and the time away doesn't count.
/// </summary>
public sealed partial class ActivityTrackerService
{
    // Walking keeps a session alive; saving that on every step would rewrite its header several times a second.
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(30);
    // The active play session; null while none is running.
    private ActivityPlaySession? playSession;
    // The character whose play session is running or would start.
    private ActivityOwner? playOwner;
    // When the active play session started or last continued. A run never starts before it.
    private DateTime playOpenedAt;
    // The last run of the active play session ended here.
    private DateTime? lastRunClosedAt;
    private bool playSessionDirty;
    private readonly Dictionary<Guid, ActivityPlaySession> deferredPlaySessions = new();

    public Guid? CurrentPlaySessionId { get { lock (sync) return playSession?.Id; } }
    /// <summary>Recording is on, a character is known and no play session is running.</summary>
    public bool CanStartSession { get { lock (sync) return enabled && playOwner is not null && playSession is null; } }

    /// <summary>Starts a play session now. Its first run starts with it, or on leaving town.</summary>
    public void StartSession()
    {
        lock (sync)
        {
            if (!enabled || playOwner is null || playSession is not null) return;
            var now = DateTime.UtcNow;
            // A new session replaces one the game or app interrupted.
            persistence.TakeRecentPlaySession(playOwner, now);
            ActivatePlaySession(now, now, resume: false);
            OpenEntryRun(now);
            SaveHeaders();
        }
        _ = persistence.FlushAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ends the play session now; the next activity starts a new one.</summary>
    public void EndSession(Guid? expectedPlaySessionId = null)
    {
        lock (sync)
        {
            if (expectedPlaySessionId is { } expected && playSession?.Id != expected) return;
            var now = DateTime.UtcNow;
            ResolvePending(now);
            ClosePlaySession(now, ActivityEndReason.Finished);
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

    // Each join names the character. A new one ends the old character's session; the same one back
    // after an interruption continues its session.
    private void OpenPlaySessionFor(ActivityOwner? owner, DateTime at)
    {
        if (owner is null) return;
        if (owner != playOwner)
        {
            if (playOwner is not null) ClosePlaySession(at, ActivityEndReason.CharacterChanged);
            playOwner = owner;
        }
        ResumePlaySession(at);
    }

    // The same character back within the idle limit continues a session the game or app interrupted.
    private void ResumePlaySession(DateTime at)
    {
        if (!enabled || playOwner is null || playSession is not null
            || persistence.TakeRecentPlaySession(playOwner, at) is not { } recent) return;
        var away = recent.EndedAtUtc is { } left && at > left ? (at - left).TotalSeconds : 0;
        playSession = recent with
        {
            Revision = recent.Revision + 1,
            State = ActivityState.Open,
            EndReason = ActivityEndReason.None,
            EndedAtUtc = null,
            LastActivityAtUtc = at,
            AwaySeconds = recent.AwaySeconds + away
        };
        playOpenedAt = at;
        lastRunClosedAt = null;
        playSessionDirty = true;
        headersDirty = true;
    }

    // The first run of a session starts it, unless it continues an interrupted one.
    private ActivityPlaySession? ActivatePlaySession(DateTime runStartedAt, DateTime at, bool resume = true)
    {
        if (playOwner is null) return null;
        if (resume) ResumePlaySession(at);
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
            playOpenedAt = runStartedAt;
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

    // Moving shows the player is still there, so the session doesn't end as idle.
    private void MarkPresence(DateTime at)
    {
        if (playSession is { } active && at - active.LastActivityAtUtc >= PresenceInterval) MarkPlayActivity(at);
    }

    // Closes the open run and the play session at the given time. The run can end for its own reason.
    private void ClosePlaySession(DateTime at, ActivityEndReason reason, ActivityEndReason? runReason = null)
    {
        if (session is not null) CloseSession(at > session.StartedAtUtc ? at : session.StartedAtUtc, runReason ?? reason);
        if (playSession is { } closing)
        {
            var ended = closing with
            {
                Revision = closing.Revision + 1,
                State = reason == ActivityEndReason.Interrupted ? ActivityState.Interrupted : ActivityState.Closed,
                EndReason = reason,
                EndedAtUtc = lastRunClosedAt is { } runEnd && runEnd > at ? runEnd : at
            };
            QueuePlaySession(ended);
            // An interrupted session waits for the same character to come back.
            if (reason == ActivityEndReason.Interrupted) persistence.RememberRecentPlaySession(ended);
        }
        playSession = null;
        playSessionDirty = false;
        lastRunClosedAt = null;
        resumable = null;
    }

    private void ExpirePlaySession(DateTime at)
    {
        // Without moving or acting for the idle limit, the session ends at the last of those.
        if (playSession is { } active && at >= active.LastActivityAtUtc + ActivityPlaySessionProjection.IdleLimit)
            ClosePlaySession(active.LastActivityAtUtc, ActivityEndReason.Inactivity);
    }

    // Without game traffic for a while the game was closed or the connection lost. The session is
    // interrupted where the traffic stopped and continues if the character is back soon.
    private void InterruptIfGameLeft(DateTime now)
    {
        if (playSession is not { } active) return;
        var lastTraffic = player.LastPacketTime;
        if (now - lastTraffic < ActivityPlaySessionProjection.GameLeftAfter) return;
        ResolvePending(now);
        // The run ended where the player left, not part way through a recording.
        ClosePlaySession(lastTraffic > active.StartedAtUtc ? lastTraffic : active.StartedAtUtc, ActivityEndReason.Interrupted,
            ActivityEndReason.CaptureGap);
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
