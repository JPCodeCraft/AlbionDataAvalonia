using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Farming.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace AlbionDataAvalonia.Farming;

/// <summary>Tracks island observations and reversible assumptions from confirmed demolition timers.</summary>
public sealed class FarmingTrackerService : IDisposable
{
    private const int MaxTransientEntries = 4096;
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);
    private readonly object sync = new();
    private readonly AuthService auth;
    private readonly PlayerState player;
    private readonly FarmingUploadService uploader;
    private readonly SettingsManager settings;
    private readonly Dictionary<long, FarmingObjectObservation> objects = new();
    private readonly Dictionary<long, (FarmingObjectState State, DateTime ObservedAt)> pendingStates = new();
    private readonly Dictionary<(short Operation, long Id), PendingAction> actions = new();
    private readonly Dictionary<(short Operation, long Id), DateTime> completedActions = new();
    private readonly Dictionary<string, IslandMetadata> islandMetadata = new();
    private readonly HashSet<string> removedObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<PlotKey, PendingDemolition> demolitions = new();
    private readonly Dictionary<long, int?> plotRenovations = new();
    private readonly Timer demolitionTimer;
    private bool disposed;
    private FarmingIslandObservation? island;
    private string? accountId;
    private int? serverId;
    private long localObjectId;
    private bool joining = true;
    private string? pendingDestination;

    public FarmingTrackerService(AuthService auth, PlayerState player, FarmingUploadService uploader, SettingsManager settings)
    {
        this.auth = auth;
        this.player = player;
        this.uploader = uploader;
        this.settings = settings;
        accountId = auth.FirebaseUserId;
        serverId = player.AlbionServer?.Id;
        auth.FirebaseUserChanged += OnAuthChanged;
        settings.UserSettings.PropertyChanged += OnSettingsChanged;
        demolitionTimer = new Timer(_ => ExpireDemolitions(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            demolitions.Clear();
        }
        demolitionTimer.Dispose();
        auth.FirebaseUserChanged -= OnAuthChanged;
        settings.UserSettings.PropertyChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.AfmIslandTrackerEnabled)) return;
        lock (sync)
        {
            demolitions.Clear();
            islandMetadata.Clear();
            BeginTransition();
        }
    }

    public void ResetTransientState()
    {
        lock (sync)
        {
            BeginTransition();
        }
    }

    public bool CanObserveObjects()
    {
        lock (sync)
        {
            return CanObserve() && (joining || island is not null);
        }
    }

    private void OnAuthChanged(FirebaseAuthResponse? user)
    {
        lock (sync)
        {
            if (accountId == user?.LocalId) return;
            accountId = user?.LocalId;
            demolitions.Clear();
            islandMetadata.Clear();
            BeginTransition();
        }
    }

    private bool CanObserve()
    {
        if (disposed || !settings.UserSettings.AfmIslandTrackerEnabled || string.IsNullOrEmpty(accountId) || auth.FirebaseUserId != accountId) return false;
        var currentServer = player.AlbionServer?.Id;
        if (serverId != currentServer)
        {
            serverId = currentServer;
            islandMetadata.Clear();
            BeginTransition();
        }
        return serverId is >= 1 and <= 3;
    }

    private void BeginTransition()
    {
        island = null;
        localObjectId = 0;
        joining = true;
        pendingDestination = null;
        objects.Clear();
        removedObjects.Clear();
        plotRenovations.Clear();
        // Confirmed countdowns belong to stable building identities, not a visit's
        // session IDs. Only incomplete requests are discarded at a transition.
        foreach (var key in demolitions.Where(entry => !entry.Value.Confirmed || entry.Value.Duration is null)
            .Select(entry => entry.Key).ToArray()) demolitions.Remove(key);
        pendingStates.Clear();
        actions.Clear();
        completedActions.Clear();
    }

    public void OnLeave(long objectId) => Observe(() =>
    {
        if (localObjectId != 0 && objectId == localObjectId)
        {
            BeginTransition();
            return;
        }
        // Leave can be visibility loss even after the deadline. Keep the stable
        // countdown; its expiry produces an assumption, never a confirmed removal.
        pendingStates.Remove(objectId);
        plotRenovations.Remove(objectId);
        objects.Remove(objectId);
    });

    public void OnJoinStarted() => Observe(() =>
    {
        if (!joining) BeginTransition();
    });

    public void OnJoin(JoinResponse value) => Observe(() => ObserveJoin(value));
    public void OnClusterChanged(ChangeClusterResponse value) => Observe(() => ObserveCluster(value));
    public void OnIslandList(GetIslandInfosResponse value) => Observe(() => ObserveIslandList(value));
    public void OnBuilding(NewBuildingEvent value) => Observe(() => ObserveBuilding(value));
    public void OnFarmable(FarmableObjectInfoEvent value) => Observe(() => ObserveFarmable(value));

    public void OnRenovationRequest(BuildingRenovationRequest value) => Observe(() =>
    {
        if (island is null || value.ObjectId is not { } id || value.State is not { } state
            || !objects.TryGetValue(id, out var plot) || plot.Kind != "plot" || (plot.Removed && plot.RemovalAssumed != true)) return;
        var key = KeyOf(plot);
        // Requests never prove success. Cancelling immediately disarms the timer.
        if (state != 4) demolitions.Remove(key);
        else if (!demolitions.ContainsKey(key) && demolitions.Count < MaxTransientEntries)
            demolitions[key] = new PendingDemolition(plot, Stopwatch.GetTimestamp(), false, null);
    });

    public void OnFarmBuilding(FarmBuildingInfoEvent value) => Observe(() =>
    {
        if (value.ObjectId is not { } id || !objects.TryGetValue(id, out var plot) || island is null
            || !demolitions.TryGetValue(KeyOf(plot), out var pendingDemolition)) return;
        // Cancellation omits the start but retains the old end: never use the end alone.
        TimeSpan? duration = value.RenovationStartedAt is { } start && value.RenovationEndsAt is { } end
            && end > start && end - start <= TimeSpan.FromDays(7) ? end - start : null;
        if (duration is null) demolitions.Remove(KeyOf(plot));
        else demolitions[KeyOf(plot)] = pendingDemolition with { Duration = duration };
    });

    public void OnActionRequest(OperationCodes operation, FarmingActionRequest value) => Observe(() =>
    {
        if (island is null || value.RequestId is not { } requestId || value.TargetId is not { } target) return;
        PruneActions();
        var key = ((short)operation, requestId);
        if (actions.ContainsKey(key) || completedActions.ContainsKey(key)) return;
        objects.TryGetValue(target, out var source);
        actions[key] = new PendingAction(Guid.NewGuid().ToString(), DateTime.UtcNow, island, target, source);
    });

    public void OnActionResponse(OperationCodes operation, FarmingActionResponse value) => Observe(() =>
    {
        if (value.RequestId is not { } requestId) return;
        if (value.ReturnCode != 0)
        {
            actions.Remove(((short)operation, requestId));
            return;
        }
        CompleteAction(operation, value);
    });

    private void Observe(Action action)
    {
        lock (sync)
        {
            if (!CanObserve()) return;
            try { action(); }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                // A changed/malformed farming payload must not interrupt the other packet handlers.
                Log.Debug(ex, "Skipped an unsupported farming packet");
            }
        }
    }

    private void ObserveJoin(JoinResponse value)
    {
        var islandId = value.IslandId;
        var characterId = value.userGuid?.ToString();
        var characterName = string.IsNullOrWhiteSpace(value.playerName) ? null : value.playerName.Trim();
        if (islandId is null || characterId is null || characterName is null || characterName.Length > 100)
        {
            BeginTransition();
            joining = false;
            localObjectId = value.userObjectId;
            return;
        }
        // A repeated Join must never flush the current visit's cache again. A new
        // session without an observed transition cannot adopt the previous session's objects.
        if (!joining)
        {
            if (island?.IslandId == islandId && island.CharacterId == characterId
                && localObjectId == value.userObjectId) return;
            BeginTransition();
        }
        if (pendingDestination is not null && AlbionLocations.GetIslandId(pendingDestination) != islandId)
            BeginTransition();
        if (localObjectId != value.userObjectId)
        {
            actions.Clear();
            completedActions.Clear();
        }
        joining = false;
        pendingDestination = null;
        localObjectId = value.userObjectId;
        islandMetadata.TryGetValue(islandId, out var metadata);
        island = new FarmingIslandObservation
        {
            ServerId = serverId!.Value,
            CharacterId = characterId,
            CharacterName = characterName,
            IslandId = islandId,
            ObservedAt = DateTime.UtcNow,
            OwnerName = metadata?.OwnerName,
            HomeCluster = metadata?.HomeCluster ?? value.IslandHomeCluster,
            LayoutId = metadata?.LayoutId
        };
        uploader.EnqueueIsland(accountId!, island);
        foreach (var (id, observation) in objects.ToArray())
        {
            var contextual = ApplyContext(observation);
            if (contextual.State is { } state)
                contextual = contextual with { State = state with { HasPremium = state.HasPremium ?? player.HasPremium } };
            objects[id] = contextual;
            uploader.EnqueueObject(accountId!, contextual);
        }
    }

    private void ObserveCluster(ChangeClusterResponse value)
    {
        var id = AlbionLocations.GetIslandId(value.Destination);
        var destination = value.Destination;
        if (destination is null) return;
        if (id is not null)
        {
            islandMetadata.TryGetValue(id, out var previous);
            islandMetadata[id] = new IslandMetadata(
                value.OwnerName ?? previous?.OwnerName,
                previous?.HomeCluster,
                value.LayoutId ?? previous?.LayoutId);
        }
        if (island?.IslandId == id && island is not null)
        {
            if (islandMetadata.TryGetValue(id!, out var metadata))
            {
                island = island with
                {
                    ObservedAt = DateTime.UtcNow,
                    OwnerName = metadata.OwnerName,
                    HomeCluster = metadata.HomeCluster ?? island.HomeCluster,
                    LayoutId = metadata.LayoutId ?? island.LayoutId
                };
                uploader.EnqueueIsland(accountId!, island);
            }
            return;
        }
        if (!joining || (pendingDestination is not null && pendingDestination != destination)) BeginTransition();
        pendingDestination = destination;
    }

    private void ObserveIslandList(GetIslandInfosResponse value)
    {
        for (var i = 0; i < value.IslandIds.Length; i++)
        {
            var id = value.IslandIds[i].ToString();
            islandMetadata.TryGetValue(id, out var previous);
            islandMetadata[id] = new IslandMetadata(value.OwnerNames[i], value.HomeClusters[i], previous?.LayoutId);
        }
        // The travel list supplies metadata, not evidence that every listed island was visited.
        if (island is not null && islandMetadata.TryGetValue(island.IslandId, out var metadata))
        {
            island = island with { OwnerName = metadata.OwnerName, HomeCluster = metadata.HomeCluster };
            uploader.EnqueueIsland(accountId!, island);
        }
    }

    private void ObserveBuilding(NewBuildingEvent packet)
    {
        if ((!joining && island is null) || packet.Object is not { } observed) return;
        var sessionId = packet.SessionId;
        var stableId = observed.ObjectId;
        var name = observed.UniqueName;
        var rotation = observed.Rotation;
        objects.TryGetValue(sessionId, out var previous);
        if (removedObjects.Contains(stableId)) return;
        var lifecycleChanged = false;
        if (observed.Kind == "plot")
        {
            // Cancellation also supplies fresh positive evidence for the backend.
            lifecycleChanged = !plotRenovations.TryGetValue(sessionId, out var lastRenovation)
                || lastRenovation != packet.RenovationState;
            plotRenovations[sessionId] = packet.RenovationState;
            UpdateDemolitionFromBuilding(observed, packet.RenovationState);
            foreach (var old in objects.Values.Where(candidate => candidate.Kind == "plot" && !candidate.Removed
                && candidate.ObjectId != stableId && FarmingPlotSlots.SamePosition(candidate, observed)).ToArray())
                RemovePlot(old, observed.ObservedAt);
        }
        // The same occupant's repeated metadata is not a fresh growth snapshot.
        if (previous?.ObjectId == stableId && ((previous.Removed && previous.RemovalAssumed != true)
            || (!previous.Removed && !lifecycleChanged && previous.UniqueName == name && previous.PositionX == observed.PositionX
                && previous.PositionY == observed.PositionY && previous.Rotation == rotation))) return;
        if (objects.Count >= MaxTransientEntries && previous is null) return;
        var bufferedState = island is null && previous?.ObjectId == stableId ? previous.State : null;
        var value = observed with
        {
            ObservedAt = bufferedState is null ? observed.ObservedAt : previous!.ObservedAt,
            State = bufferedState
        };
        var hasNewState = pendingStates.Remove(sessionId, out var pending);
        if (hasNewState) value = value with
        {
            State = pending.State with { HasPremium = pending.State.HasPremium ?? (joining ? null : player.HasPremium) },
            ObservedAt = pending.ObservedAt
        };
        value = ApplyContext(value);
        if (value.Kind == "farmable")
            value = value with { PlotObjectId = FindPlot(value)?.ObjectId };
        objects[sessionId] = value;
        if (island is not null) uploader.EnqueueObject(accountId!, hasNewState ? value : value with { State = null });
        if (value.Kind == "plot")
        {
            // Animal metadata may precede its plot. Bind and resend only unbound entries;
            // never attach the previous building's animals to its replacement.
            foreach (var (id, child) in objects.Where(entry => !entry.Value.Removed
                && entry.Value.PlotObjectId is null && FarmingPlotSlots.Contains(value, entry.Value)).ToArray())
            {
                var bound = child with { PlotObjectId = value.ObjectId };
                objects[id] = bound;
                if (island is not null) uploader.EnqueueObject(accountId!, bound);
            }
        }
    }

    private void ObserveFarmable(FarmableObjectInfoEvent packet)
    {
        if ((!joining && island is null) || packet.State is not { } state) return;
        state = state with { HasPremium = joining ? null : player.HasPremium };
        var id = packet.ObjectId;
        var observedAt = DateTime.UtcNow;
        if (!objects.TryGetValue(id, out var value))
        {
            if (pendingStates.Count < MaxTransientEntries) pendingStates[id] = (state, observedAt);
            return;
        }
        if (value.Removed) return;
        value = value with { State = state, ObservedAt = observedAt };
        objects[id] = value;
        if (island is not null) uploader.EnqueueObject(accountId!, value);
    }

    private static PlotKey KeyOf(FarmingObjectObservation plot) => new(plot.ServerId, plot.IslandId, plot.ObjectId);

    private void UpdateDemolitionFromBuilding(FarmingObjectObservation observed, int? renovation)
    {
        if (renovation is null) return;
        // Stable UUIDs also correlate snapshots buffered before Join supplies the
        // visit context. Session IDs may have changed since starting demolition.
        foreach (var (key, pendingDemolition) in demolitions.Where(entry => entry.Key.ServerId == serverId
            && entry.Key.ObjectId == observed.ObjectId && (island is null || entry.Key.IslandId == island.IslandId)).ToArray())
        {
            if (renovation != 4) demolitions.Remove(key);
            else
            {
                var evidence = observed with
                {
                    ServerId = pendingDemolition.Plot.ServerId,
                    IslandId = pendingDemolition.Plot.IslandId,
                    CharacterId = pendingDemolition.Plot.CharacterId,
                    CharacterName = pendingDemolition.Plot.CharacterName
                };
                demolitions[key] = pendingDemolition with { Confirmed = true, Plot = evidence };
            }
        }
    }

    private void ExpireDemolitions()
    {
        lock (sync)
        {
            if (disposed || !settings.UserSettings.AfmIslandTrackerEnabled || string.IsNullOrEmpty(accountId)
                || accountId != auth.FirebaseUserId) return;
            foreach (var (key, pendingDemolition) in demolitions.ToArray())
            {
                var elapsed = Stopwatch.GetElapsedTime(pendingDemolition.RequestedAt);
                if (!pendingDemolition.Confirmed || pendingDemolition.Duration is not { } duration)
                {
                    if (elapsed > RequestLifetime) demolitions.Remove(key);
                    continue;
                }
                if (elapsed < duration) continue;
                var now = DateTime.UtcNow;
                var assumed = pendingDemolition.Plot with
                {
                    Removed = true,
                    RemovalAssumed = true,
                    State = null,
                    ObservedAt = now < pendingDemolition.Plot.ObservedAt ? pendingDemolition.Plot.ObservedAt : now,
                    OccupantObservedAt = pendingDemolition.Plot.ObservedAt
                };
                if (!uploader.EnqueueObject(accountId, assumed)) continue;
                demolitions.Remove(key);
                // No retired-UUID marker and no destructive child cleanup: a fresh
                // building snapshot must be able to reverse the assumption.
                foreach (var (id, cached) in objects.Where(entry => entry.Value.Kind == "plot"
                    && KeyOf(entry.Value) == key).ToArray()) objects[id] = assumed;
            }
        }
    }

    private FarmingObjectObservation? FindPlot(FarmingObjectObservation farmable) => objects.Values
        .FirstOrDefault(plot => !plot.Removed && FarmingPlotSlots.Contains(plot, farmable));

    private void RemovePlot(FarmingObjectObservation plot, DateTime observedAt)
    {
        if (!removedObjects.Add(plot.ObjectId)) return;
        demolitions.Remove(KeyOf(plot));
        var removed = plot with { Removed = true, RemovalAssumed = null, State = null, ObservedAt = observedAt, OccupantObservedAt = plot.ObservedAt };
        if (island is not null) uploader.EnqueueObject(accountId!, removed);
        foreach (var (id, value) in objects.Where(entry => entry.Value.ObjectId == plot.ObjectId
            || entry.Value.PlotObjectId == plot.ObjectId).ToArray())
        {
            objects.Remove(id);
            pendingStates.Remove(id);
            plotRenovations.Remove(id);
            removedObjects.Add(value.ObjectId);
        }
    }

    private FarmingObjectObservation ApplyContext(FarmingObjectObservation value) => island is null ? value : value with
    {
        ServerId = island.ServerId,
        CharacterId = island.CharacterId,
        CharacterName = island.CharacterName,
        IslandId = island.IslandId
    };

    private void CompleteAction(OperationCodes operationCode, FarmingActionResponse packet)
    {
        if (packet.RequestId is not { } requestId) return;
        PruneActions();
        var key = ((short)operationCode, requestId);
        if (!actions.Remove(key, out var action)) return;
        completedActions[key] = DateTime.UtcNow;
        // A successful removal is known even if a changed item payload cannot be decoded.
        if (operationCode != OperationCodes.FarmableGetProduct && action.Source is not null)
        {
            var removed = action.Source with
            {
                Removed = true,
                RemovalAssumed = null,
                State = null,
                ObservedAt = DateTime.UtcNow,
                OccupantObservedAt = action.Source.ObservedAt
            };
            removedObjects.Add(removed.ObjectId);
            if (removed.Kind == "plot") demolitions.Remove(KeyOf(removed));
            uploader.EnqueueObject(accountId!, removed);
            if (objects.TryGetValue(action.Target, out var current) && current.ObjectId == removed.ObjectId)
                objects[action.Target] = removed;
        }
        else if (operationCode == OperationCodes.FarmableGetProduct
            && objects.TryGetValue(action.Target, out var productObject)
            && productObject.ObjectId == action.Source?.ObjectId && !productObject.Removed
            && productObject.ObservedAt <= action.RequestedAt && productObject.State is { } productState
            && (productState.ProductReady.HasValue || productState.ProductReadyAt.HasValue
                || productState.ProductProgressSeconds.HasValue || productState.ProductProgressUpdatedAt.HasValue))
        {
            // Collection invalidates the old timer. A snapshot received after the
            // request may already describe the next cycle and must be preserved.
            var updated = productObject with
            {
                ObservedAt = DateTime.UtcNow,
                State = productState with
                {
                    ProductReady = null,
                    ProductReadyAt = null,
                    ProductProgressSeconds = null,
                    ProductProgressUpdatedAt = null
                }
            };
            objects[action.Target] = updated;
            uploader.EnqueueObject(accountId!, updated);
        }
        var operation = operationCode switch
        {
            OperationCodes.FarmableHarvest => "harvest",
            OperationCodes.FarmableFinishGrownItem => "finish",
            OperationCodes.FarmableGetProduct => "product",
            _ => null
        };
        if (operation is not null)
        {
            if (packet.Items.Count > 0)
            {
                uploader.EnqueuePickup(accountId!, new FarmingPickup
                {
                    ServerId = action.Island.ServerId,
                    CharacterId = action.Island.CharacterId,
                    CharacterName = action.Island.CharacterName,
                    IslandId = action.Island.IslandId,
                    EventId = action.EventId,
                    OccurredAt = DateTime.UtcNow,
                    Operation = operation,
                    SourceObjectId = action.Source?.ObjectId,
                    Items = packet.Items
                });
            }
        }
    }

    private void PruneActions()
    {
        var cutoff = DateTime.UtcNow - RequestLifetime;
        foreach (var key in actions.Where(p => p.Value.RequestedAt < cutoff).Select(p => p.Key).ToArray()) actions.Remove(key);
        foreach (var key in completedActions.Where(p => p.Value < cutoff).Select(p => p.Key).ToArray()) completedActions.Remove(key);
    }

    private readonly record struct PlotKey(int ServerId, string IslandId, string ObjectId);
    private sealed record PendingDemolition(FarmingObjectObservation Plot, long RequestedAt, bool Confirmed, TimeSpan? Duration);
    private sealed record IslandMetadata(string? OwnerName, string? HomeCluster, string? LayoutId);
    private sealed record PendingAction(string EventId, DateTime RequestedAt, FarmingIslandObservation Island, long Target, FarmingObjectObservation? Source);
}
