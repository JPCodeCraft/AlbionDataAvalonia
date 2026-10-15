using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace AlbionDataAvalonia.Activity;

/// <summary>A single ordered journal writer; queued objects are immutable metadata snapshots.</summary>
public sealed partial class ActivityPersistence : IAsyncDisposable
{
    private const int Capacity = 20000;
    private readonly Func<LocalContext> contextFactory;
    private readonly Channel<WriteCommand> commands = Channel.CreateBounded<WriteCommand>(new BoundedChannelOptions(Capacity)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource shutdown = new();
    private readonly SemaphoreSlim priceFillGate = new(1, 1);
    private readonly HashSet<Guid> deletedSessions = [];
    private readonly object acceptedSync = new();
    // Includes queued and currently committing mutations, bounded by the existing writer capacity.
    // A live read takes this snapshot before its database snapshot, so commits cannot create a hole.
    private readonly List<WriteCommand> acceptedWrites = new();
    private readonly Task worker;
    private int pending;
    private bool disposed;
    private ActivityRecordingStatus status = new(false, 0, null);

    public ActivityPersistence(Func<LocalContext>? contextFactory = null)
    {
        this.contextFactory = contextFactory ?? (() => new LocalContext());
        worker = Task.Run(RunAsync);
    }

    public ActivityRecordingStatus Status => Volatile.Read(ref status) with { PendingChanges = Volatile.Read(ref pending) };
    public event Action<ActivityRecordingStatus>? StatusChanged;
    public event Action<Guid>? SessionDeleted;
    public event Action? UnassignedLootDeleted;
    public bool EnqueueSession(ActivitySession session) => Enqueue(new SessionCommand(session));
    public bool EnqueueEncounter(ActivityEncounter encounter) => Enqueue(new EncounterCommand(encounter));
    public bool Create(ActivityEvent observation) => Enqueue(new EventCommand(observation, false));
    public bool Amend(ActivityEvent observation) => Enqueue(new EventCommand(observation, true));
    public bool Retract(Guid eventId, int revision) => Enqueue(new RetractCommand(eventId, revision));

    public async Task FillMissingLootPricesAsync(IReadOnlyCollection<ActivityLootPrice> prices, CancellationToken cancellationToken = default)
    {
        // Database-only price patches commute with collector revisions; a stale snapshot cannot overwrite a filled price.
        var snapshots = prices.Where(x => x.ItemId > 0 && (x.Quality > 0 || x.LootBasis && x.Quality == 0) && x.UnitValue > 0)
            .DistinctBy(x => (x.Server, x.ItemId, x.Quality, x.LootBasis)).ToArray();
        if (snapshots.Length == 0) return;
        await priceFillGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var price in snapshots)
            {
                var quality = price.Quality;
                while (!disposed)
                {
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                    var missing = await ReadAsync(async readCancellation =>
                    {
                        await using var context = contextFactory();
                        var unpriced = context.ActivityEvents.AsNoTracking().Where(x => !x.IsRetracted &&
                            x.Server == price.Server && x.ItemId == price.ItemId && x.EstimatedUnitValue == null &&
                            (x.Quantity == 0 || x.Quantity <= long.MaxValue / price.UnitValue));
                        // Loot uses its own valuation basis; quality 0 is the average for loot without quality.
                        if (!price.LootBasis)
                            unpriced = unpriced.Where(x => (x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.FishingReward) &&
                                (x.Quality == quality || x.Quality == null && quality == 1));
                        else if (quality == 0) unpriced = unpriced.Where(x => x.Kind == ActivityEventKind.Loot && x.Quality == null);
                        else unpriced = unpriced.Where(x => x.Kind == ActivityEventKind.Loot && x.Quality == quality);
                        return await unpriced.OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.Id).Take(250).ToArrayAsync(readCancellation);
                    }, cancellationToken).ConfigureAwait(false);
                    if (missing.Length == 0) break;
                    foreach (var observation in missing)
                    {
                        var patch = new EventCommand(ActivityItemValuationService.WithPrice(observation, price.UnitValue,
                            price.ObservedAtUtc, price.Quality), true, true);
                        while (!Enqueue(patch)) await FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    // Let normal ingress interleave between bounded historical chunks.
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally { priceFillGate.Release(); }
    }

    private bool Enqueue(WriteCommand command)
    {
        if (disposed || Status.IsSuspended) return false;
        if (Interlocked.Increment(ref pending) > Capacity)
        {
            Interlocked.Decrement(ref pending);
            PublishStatus(true, "Activity recording paused: the database write queue is full.");
            return false;
        }
        lock (acceptedSync)
        {
            if (commands.Writer.TryWrite(command))
            {
                acceptedWrites.Add(command);
                Interlocked.Increment(ref readVersion);
                return true;
            }
        }
        Interlocked.Decrement(ref pending);
        PublishStatus(true, "Activity recording paused: the database write queue is full.");
        return false;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => ControlAsync(new FlushCommand(NewCompletion()), cancellationToken);
    public Task RecoverInterruptedAsync(CancellationToken cancellationToken = default) => ControlAsync(new RecoverCommand(NewCompletion()), cancellationToken);
    public Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) => ControlAsync(new DeleteSessionCommand(sessionId, NewCompletion()), cancellationToken);
    public Task DeleteUnassignedLootAsync(CancellationToken cancellationToken = default) => ControlAsync(new DeleteLootCommand(NewCompletion()), cancellationToken);
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private async Task ControlAsync(ControlCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Interlocked.Increment(ref pending);
        try { await commands.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false); }
        catch { Interlocked.Decrement(ref pending); throw; }
        await command.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        var batch = new List<WriteCommand>(250);
        ControlCommand? activeControl = null;
        try
        {
            while (await commands.Reader.WaitToReadAsync(shutdown.Token))
            {
                if (!commands.Reader.TryRead(out var first)) continue;
                batch.Add(first);
                var deadline = DateTime.UtcNow.AddSeconds(1);
                while (batch.Count < 250 && batch[^1] is not ControlCommand)
                {
                    if (commands.Reader.TryRead(out var next)) { batch.Add(next); continue; }
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    var ready = commands.Reader.WaitToReadAsync(shutdown.Token).AsTask();
                    if (await Task.WhenAny(ready, Task.Delay(remaining, shutdown.Token)) != ready || !await ready) break;
                }
                var control = batch.LastOrDefault() as ControlCommand;
                activeControl = control;
                if (control is not null) batch.RemoveAt(batch.Count - 1);
                var retry = 0;
                while (true)
                {
                    try
                    {
                        if (batch.Count > 0)
                        {
                            try { await PersistAsync(batch, shutdown.Token); }
                            // A validation rejection repeats on every retry and would suspend recording
                            // for good. Write the batch one by one and drop only the rejected writes.
                            catch (InvalidOperationException) { await PersistEachAsync(batch, shutdown.Token); }
                        }
                        if (control is not null) await ExecuteControlAsync(control, shutdown.Token);
                        lock (acceptedSync)
                        {
                            foreach (var accepted in batch) acceptedWrites.Remove(accepted);
                            if (batch.Count > 0) Interlocked.Increment(ref readVersion);
                        }
                        Interlocked.Add(ref pending, -(batch.Count + (control is null ? 0 : 1)));
                        PublishStatus(false, null);
                        control?.Completion.TrySetResult();
                        break;
                    }
                    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        PublishStatus(true, $"Activity recording paused: {ex.Message}");
                        retry++;
                        await Task.Delay(TimeSpan.FromSeconds(retry == 1 ? 1 : retry == 2 ? 2 : 5), shutdown.Token);
                    }
                }
                batch.Clear();
                activeControl = null;
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            activeControl?.Completion.TrySetCanceled();
            foreach (var control in batch.OfType<ControlCommand>()) control.Completion.TrySetCanceled();
            while (commands.Reader.TryRead(out var command))
                if (command is ControlCommand control) control.Completion.TrySetCanceled();
        }
    }

    private void PublishStatus(bool suspended, string? error)
    {
        var next = new ActivityRecordingStatus(suspended, Volatile.Read(ref pending), error);
        var previous = Interlocked.Exchange(ref status, next);
        if (previous.IsSuspended != next.IsSuspended || previous.Error != next.Error)
        {
            try { StatusChanged?.Invoke(next); }
            catch { /* A UI subscriber cannot interrupt accepted journal writes. */ }
        }
    }

    private async Task PersistEachAsync(List<WriteCommand> batch, CancellationToken cancellationToken)
    {
        foreach (var command in batch)
        {
            try { await PersistAsync([command], cancellationToken); }
            catch (InvalidOperationException error) { Log.Warning(error, "Dropped an invalid activity write ({Command}).", command.GetType().Name); }
        }
    }

    private async Task PersistAsync(List<WriteCommand> batch, CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var sessions = new Dictionary<Guid, ActivitySession>();
        var encounters = new Dictionary<Guid, ActivityEncounter>();
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in batch)
        {
            switch (command)
            {
                case SessionCommand header when !deletedSessions.Contains(header.Value.Id):
                    var oldSession = await context.ActivitySessions.FindAsync([header.Value.Id], cancellationToken);
                    if (oldSession is not null && oldSession.Revision >= header.Value.Revision) break;
                    var session = oldSession is null ? header.Value with
                    { PersonalSummaryJson = EmptySummary, PartySummaryJson = EmptySummary, ObservedSummaryJson = EmptySummary } : header.Value with
                    {
                        PersonalSummaryJson = oldSession.PersonalSummaryJson,
                        PartySummaryJson = oldSession.PartySummaryJson,
                        ObservedSummaryJson = oldSession.ObservedSummaryJson,
                        LastObservedAtUtc = oldSession.LastObservedAtUtc > header.Value.LastObservedAtUtc ? oldSession.LastObservedAtUtc : header.Value.LastObservedAtUtc
                    };
                    if (oldSession is null) context.ActivitySessions.Add(session);
                    else { context.Entry(oldSession).CurrentValues.SetValues(session); session = oldSession; }
                    sessions[session.Id] = session;
                    break;
                case EncounterCommand header when !deletedSessions.Contains(header.Value.SessionId):
                    var oldEncounter = await context.ActivityEncounters.FindAsync([header.Value.Id], cancellationToken);
                    if (oldEncounter is not null && oldEncounter.Revision >= header.Value.Revision) break;
                    var encounter = oldEncounter is null ? header.Value with
                    { PersonalSummaryJson = EmptySummary, PartySummaryJson = EmptySummary, ObservedSummaryJson = EmptySummary } : header.Value with
                    {
                        PersonalSummaryJson = oldEncounter.PersonalSummaryJson,
                        PartySummaryJson = oldEncounter.PartySummaryJson,
                        ObservedSummaryJson = oldEncounter.ObservedSummaryJson,
                        LastObservedAtUtc = oldEncounter.LastObservedAtUtc > header.Value.LastObservedAtUtc ? oldEncounter.LastObservedAtUtc : header.Value.LastObservedAtUtc
                    };
                    if (oldEncounter is null) context.ActivityEncounters.Add(encounter);
                    else { context.Entry(oldEncounter).CurrentValues.SetValues(encounter); encounter = oldEncounter; }
                    encounters[encounter.Id] = encounter;
                    break;
                case EventCommand mutation:
                    var observation = RedirectMergedEvent(mutation.Value);
                    if (deletedEvents.Contains(observation.Id)) break;
                    if (observation.SessionId is { } deleted && deletedSessions.Contains(deleted)) break;
                    Validate(observation);
                    var previous = await context.ActivityEvents.FindAsync([observation.Id], cancellationToken);
                    if (mutation.IsAmendment && previous is null) break;
                    if (!mutation.IsAmendment && previous is not null) break;
                    if (mutation.IsPriceOnly)
                    {
                        if (previous is null || previous.IsRetracted || previous.EstimatedUnitValue is not null || observation.EstimatedUnitValue is null ||
                            previous.Server != observation.Server || previous.ItemId != observation.ItemId ||
                            previous.ItemUniqueName != observation.ItemUniqueName || previous.Quality != observation.Quality) break;
                        if (previous.Quantity > 0 && observation.EstimatedUnitValue > long.MaxValue / previous.Quantity) break;
                        observation = ActivityItemValuationService.WithPrice(previous, observation.EstimatedUnitValue.Value,
                            observation.PriceObservedAtUtc ?? DateTime.UtcNow, ActivityItemValuationService.BasisQuality(previous));
                    }
                    else if (previous is not null && previous.Revision >= observation.Revision) break;
                    if (previous is not null)
                    {
                        var frozenPrice = previous.EstimatedUnitValue is not null && previous.ItemId == observation.ItemId &&
                            previous.ItemUniqueName == observation.ItemUniqueName && previous.Quality == observation.Quality;
                        // Correlation completes the original observation; it never adopts a later session/fight.
                        observation = observation with
                        {
                            SessionId = previous.SessionId,
                            EncounterId = previous.EncounterId,
                            CapturedAtUtc = previous.CapturedAtUtc,
                            CaptureOrder = previous.CaptureOrder,
                            OriginKey = previous.OriginKey,
                            MapEpoch = previous.MapEpoch,
                            CaptureGeneration = previous.CaptureGeneration,
                            EstimatedUnitValue = frozenPrice ? previous.EstimatedUnitValue : observation.EstimatedUnitValue,
                            PriceObservedAtUtc = frozenPrice ? previous.PriceObservedAtUtc : observation.PriceObservedAtUtc
                        };
                        if (frozenPrice)
                            observation = ActivityItemValuationService.WithPrice(observation, previous.EstimatedUnitValue!.Value,
                                previous.PriceObservedAtUtc ?? DateTime.UtcNow, ActivityItemValuationService.BasisQuality(previous)) with
                            { PriceObservedAtUtc = previous.PriceObservedAtUtc };
                    }
                    else if (observation.OriginKey is { } origin &&
                        (!origins.Add(origin) || await context.ActivityEvents.AnyAsync(e => e.OriginKey == origin, cancellationToken))) break;
                    Validate(observation);
                    await ValidateGroupingAsync(context, observation, cancellationToken);
                    if (previous is not null) await ApplyContributionAsync(context, previous, -1, sessions, encounters, cancellationToken);
                    await ApplyContributionAsync(context, observation, 1, sessions, encounters, cancellationToken);
                    if (previous is null) context.ActivityEvents.Add(observation);
                    else context.Entry(previous).CurrentValues.SetValues(observation);
                    break;
                case RetractCommand retract:
                    var existing = await context.ActivityEvents.FindAsync([retract.Id], cancellationToken);
                    if (existing is null || existing.Revision >= retract.Revision || existing.IsRetracted) break;
                    await ApplyContributionAsync(context, existing, -1, sessions, encounters, cancellationToken);
                    context.Entry(existing).CurrentValues.SetValues(existing with { Revision = retract.Revision, IsRetracted = true });
                    break;
            }
        }
        var changes = new CommittedChanges();
        changes.Include(context);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        NotifyCommitted(changes);
    }

    private static void Validate(ActivityEvent observation)
    {
        if (observation.Id == Guid.Empty || observation.Revision < 1 || !double.IsFinite(observation.Amount) || observation.Amount < 0 || observation.Quantity < 0)
            throw new InvalidOperationException("Invalid activity observation.");
        if (observation.DetailsJson.Length > 32768) throw new InvalidOperationException("Activity detail exceeds the bounded payload size.");
        if (observation.EstimatedUnitValue is < 0 || (observation.EstimatedUnitValue is { } price && observation.Quantity > 0 && price > long.MaxValue / observation.Quantity))
            throw new InvalidOperationException("Invalid loot valuation.");
    }

    private static async Task ValidateGroupingAsync(LocalContext context, ActivityEvent observation, CancellationToken cancellationToken)
    {
        if (observation.EncounterId is not { } encounterId) return;
        var encounter = await context.ActivityEncounters.FindAsync([encounterId], cancellationToken);
        if (encounter is null || observation.SessionId != encounter.SessionId)
            throw new InvalidOperationException("An activity encounter must belong to the event's session.");
    }

    private static async Task ApplyContributionAsync(LocalContext context, ActivityEvent observation, int sign,
        Dictionary<Guid, ActivitySession> sessions, Dictionary<Guid, ActivityEncounter> encounters, CancellationToken cancellationToken)
    {
        if (observation.SessionId is { } sessionId)
        {
            if (!sessions.TryGetValue(sessionId, out var session))
                session = await context.ActivitySessions.FindAsync([sessionId], cancellationToken) ?? throw new InvalidOperationException("Missing activity session.");
            var replacement = session with
            {
                PersonalSummaryJson = Apply(session.PersonalSummaryJson, ActivityScope.Personal),
                PartySummaryJson = Apply(session.PartySummaryJson, ActivityScope.Party),
                ObservedSummaryJson = Apply(session.ObservedSummaryJson, ActivityScope.AllObserved),
                LastObservedAtUtc = observation.CapturedAtUtc > session.LastObservedAtUtc ? observation.CapturedAtUtc : session.LastObservedAtUtc
            };
            context.Entry(session).CurrentValues.SetValues(replacement);
            sessions[sessionId] = session;
        }
        if (observation.EncounterId is { } encounterId)
        {
            if (!encounters.TryGetValue(encounterId, out var encounter))
                encounter = await context.ActivityEncounters.FindAsync([encounterId], cancellationToken) ?? throw new InvalidOperationException("Missing activity encounter.");
            context.Entry(encounter).CurrentValues.SetValues(encounter with
            {
                PersonalSummaryJson = Apply(encounter.PersonalSummaryJson, ActivityScope.Personal),
                PartySummaryJson = Apply(encounter.PartySummaryJson, ActivityScope.Party),
                ObservedSummaryJson = Apply(encounter.ObservedSummaryJson, ActivityScope.AllObserved),
                LastObservedAtUtc = observation.CapturedAtUtc > encounter.LastObservedAtUtc ? observation.CapturedAtUtc : encounter.LastObservedAtUtc
            });
            encounters[encounterId] = encounter;
        }
        string Apply(string json, ActivityScope scope) => ActivityProjection.Serialize(
            ActivityProjection.Add(ActivityProjection.Deserialize(json), ActivityProjection.Contribution(observation, scope), sign));
    }

    private async Task ExecuteControlAsync(ControlCommand command, CancellationToken cancellationToken)
    {
        if (command is FlushCommand) return;
        if (command is MergeSessionCommand merge) { ExecuteMerge(merge); return; }
        if (command is ResumeSessionCommand resume) { ExecuteResume(resume); return; }
        if (command is ImportLegacyCommand import) { import.Result = await ImportLegacyRunsAsync(cancellationToken); return; }
        if (command is RebuildTagsCommand) { await RebuildTagsAsync(cancellationToken); return; }
        if (command is CleanupCommand cleanup) { await ExecuteCleanupAsync(cleanup, cancellationToken); return; }
        await using var context = contextFactory();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var changes = new CommittedChanges();
        switch (command)
        {
            case DeleteSessionCommand delete:
                changes.Sessions.Add(delete.Id);
                changes.Items = await context.ActivityEvents.AnyAsync(x => x.SessionId == delete.Id &&
                    (x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.FishingReward || x.Kind == ActivityEventKind.LegacyGatheringAggregate), cancellationToken);
                await context.ActivitySessions.Where(x => x.Id == delete.Id).ExecuteDeleteAsync(cancellationToken);
                break;
            case DeleteLootCommand:
                changes.Items = await context.ActivityEvents.Where(x => x.SessionId == null && x.Kind == ActivityEventKind.Loot).ExecuteDeleteAsync(cancellationToken) > 0;
                break;
            case RecoverCommand:
                while (true)
                {
                    var unfinished = await context.ActivitySessions.Where(x => x.State == ActivityState.Open).Take(100).ToListAsync(cancellationToken);
                    if (unfinished.Count == 0) break;
                    foreach (var session in unfinished)
                        context.Entry(session).CurrentValues.SetValues(session with
                        {
                            Revision = session.Revision + 1,
                            State = ActivityState.Interrupted,
                            EndReason = ActivityEndReason.Interrupted,
                            IsPartial = true,
                            ClosedAtUtc = session.LastObservedAtUtc,
                            ObservationStartedAtUtc = null,
                            ObservedDurationSeconds = ActivityProjection.GetDurationSeconds(session, session.LastObservedAtUtc)
                        });
                    changes.Include(context);
                    await context.SaveChangesAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
                while (true)
                {
                    var fights = await context.ActivityEncounters.Where(x => x.State == ActivityState.Open).Take(100).ToListAsync(cancellationToken);
                    if (fights.Count == 0) break;
                    foreach (var encounter in fights)
                        context.Entry(encounter).CurrentValues.SetValues(encounter with
                        {
                            Revision = encounter.Revision + 1,
                            State = ActivityState.Interrupted,
                            EndReason = ActivityEndReason.Interrupted,
                            ClosedAtUtc = encounter.LastObservedAtUtc
                        });
                    changes.Include(context);
                    await context.SaveChangesAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
                await RestoreKnownFameAwardsAsync(context, changes, cancellationToken);
                break;
        }
        await transaction.CommitAsync(cancellationToken);
        NotifyCommitted(changes);
        if (command is DeleteSessionCommand deleted)
        {
            lock (acceptedSync)
            {
                deletedSessions.Add(deleted.Id);
                Interlocked.Increment(ref readVersion);
            }
            try { SessionDeleted?.Invoke(deleted.Id); } catch { }
        }
        if (command is DeleteLootCommand) { try { UnassignedLootDeleted?.Invoke(); } catch { } }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        commands.Writer.TryComplete();
        shutdown.CancelAfter(TimeSpan.FromSeconds(10));
        await worker.ConfigureAwait(false);
        readsClosed = true;
        readShutdown.Cancel();
        await readGate.WaitAsync().ConfigureAwait(false);
        readGate.Release();
        readShutdown.Dispose();
        shutdown.Dispose();
    }

    private abstract record WriteCommand;
    private sealed record SessionCommand(ActivitySession Value) : WriteCommand;
    private sealed record EncounterCommand(ActivityEncounter Value) : WriteCommand;
    private sealed record EventCommand(ActivityEvent Value, bool IsAmendment, bool IsPriceOnly = false) : WriteCommand;
    private sealed record RetractCommand(Guid Id, int Revision) : WriteCommand;
    private abstract record ControlCommand(TaskCompletionSource Completion) : WriteCommand;
    private sealed record FlushCommand(TaskCompletionSource Completion) : ControlCommand(Completion);
    private sealed record RecoverCommand(TaskCompletionSource Completion) : ControlCommand(Completion);
    private sealed record DeleteSessionCommand(Guid Id, TaskCompletionSource Completion) : ControlCommand(Completion);
    private sealed record DeleteLootCommand(TaskCompletionSource Completion) : ControlCommand(Completion);
    private static string EmptySummary => ActivityProjection.Serialize(new ActivitySummary { TagVersion = ActivityClassification.TagVersion });
}
