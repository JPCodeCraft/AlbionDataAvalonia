using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Legendary.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Legendary;

public sealed class LegendaryItemTrackerService : IDisposable
{
    private static readonly TimeSpan LastSeenUpdateInterval = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly PlayerState playerState;
    private readonly SettingsManager settingsManager;
    private readonly Dictionary<long, PendingObservation> pending = new();
    private readonly Dictionary<Guid, ContainerContext> containers = new();
    private readonly Dictionary<long, ContainerContext> itemLocations = new();
    private readonly Dictionary<Guid, VaultTabMetadata> vaultTabs = new();
    private readonly HashSet<long> knownLegendaryObjects = new();
    private volatile bool isDisabled;

    public event Action? ItemsChanged;

    public LegendaryItemTrackerService(PlayerState playerState, SettingsManager settingsManager)
    {
        this.playerState = playerState;
        this.settingsManager = settingsManager;
        isDisabled = settingsManager.UserSettings.DisableAwakeningItemsTracker;
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
    }

    public async Task ObserveItemAsync(NewItem item)
    {
        if (isDisabled || item.ObjectId is not { } objectId || objectId <= 0 || !item.IsAwakened)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            var observation = GetPending(objectId);
            observation.Item = item;
            if (observation.Soul is not null)
            {
                await UpsertAsync(objectId, observation, DateTime.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ObserveSoulAsync(LegendarySoul soul)
    {
        if (isDisabled || soul.ObjectId <= 0)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            if (soul.TraitsIds.Length != soul.TraitsValues.Length)
            {
                pending.Remove(soul.ObjectId);
                Log.Warning(
                    "Ignoring legendary soul for object {ObjectId} because it has {TraitIdCount} IDs and {TraitValueCount} values",
                    soul.ObjectId,
                    soul.TraitsIds.Length,
                    soul.TraitsValues.Length);
                return;
            }

            var observation = GetPending(soul.ObjectId);
            observation.Soul = soul;
            if (observation.Item is not null)
            {
                await UpsertAsync(soul.ObjectId, observation, DateTime.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ObserveContainerAsync(long objectId, Guid containerId, Guid privateContainerId, IReadOnlyList<long> slotItems)
    {
        if (isDisabled || containerId == Guid.Empty)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            if (containers.TryGetValue(containerId, out var existingContext))
            {
                RemoveContainerContext(existingContext);
            }
            if (privateContainerId != Guid.Empty
                && containers.TryGetValue(privateContainerId, out existingContext))
            {
                RemoveContainerContext(existingContext);
            }

            var context = CreateContainerContext(objectId, containerId, privateContainerId, slotItems);
            containers[containerId] = context;
            if (privateContainerId != Guid.Empty)
            {
                containers[privateContainerId] = context;
            }
            foreach (var itemObjectId in slotItems.Where(value => value > 0))
            {
                itemLocations[itemObjectId] = context;
            }
            await ApplyContainerToStoredItemsAsync(context, slotItems, DateTime.UtcNow);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ObserveVaultAsync(
        bool isGuildVault,
        long? objectId,
        string rawLocationId,
        IReadOnlyList<Guid> tabIds,
        IReadOnlyList<string> tabNames,
        IReadOnlyList<string> tabIcons,
        IReadOnlyList<int> tabColors)
    {
        if (isDisabled)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            var locationName = AlbionLocations.ResolveLocation(rawLocationId).FriendlyName;
            for (var index = 0; index < tabIds.Count; index++)
            {
                var tabId = tabIds[index];
                if (tabId == Guid.Empty)
                {
                    continue;
                }
                vaultTabs[tabId] = new VaultTabMetadata(
                    isGuildVault ? LegendaryItemLocationKind.GuildVault : LegendaryItemLocationKind.Bank,
                    objectId,
                    rawLocationId,
                    locationName,
                    index < tabNames.Count ? tabNames[index] : string.Empty,
                    index < tabIcons.Count ? tabIcons[index] : string.Empty,
                    index < tabColors.Count ? tabColors[index] : null);
            }

            foreach (var context in containers.Values.Distinct().Where(value => value.PrivateContainerId != Guid.Empty))
            {
                if (!vaultTabs.TryGetValue(context.PrivateContainerId, out var metadata))
                {
                    continue;
                }
                ApplyMetadata(context, metadata);
                await ApplyContainerToStoredItemsAsync(context, context.SlotItems, DateTime.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ObserveInventoryPutAsync(long itemObjectId, Guid containerId)
    {
        if (isDisabled || itemObjectId <= 0 || containerId == Guid.Empty)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            if (itemLocations.TryGetValue(itemObjectId, out var previousContext)
                && !ReferenceEquals(previousContext, containers.GetValueOrDefault(containerId)))
            {
                previousContext.SlotItems = previousContext.SlotItems.Where(value => value != itemObjectId).ToArray();
            }
            if (!containers.TryGetValue(containerId, out var context))
            {
                context = CreateContainerContext(playerState.UserObjectId, containerId, Guid.Empty, [itemObjectId]);
                containers[containerId] = context;
            }
            else if (!context.SlotItems.Contains(itemObjectId))
            {
                context.SlotItems = [.. context.SlotItems, itemObjectId];
            }
            itemLocations[itemObjectId] = context;
            if (knownLegendaryObjects.Contains(itemObjectId))
            {
                await ApplyContainerToStoredItemsAsync(context, [itemObjectId], DateTime.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ObserveInventoryDeleteAsync(long itemObjectId)
    {
        if (isDisabled || itemObjectId <= 0)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            if (itemLocations.Remove(itemObjectId, out var context))
            {
                context.SlotItems = context.SlotItems.Where(value => value != itemObjectId).ToArray();
            }
            if (knownLegendaryObjects.Contains(itemObjectId))
            {
                await TouchStoredItemAsync(itemObjectId, DateTime.UtcNow);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DetachContainerAsync(Guid containerId)
    {
        if (isDisabled || containerId == Guid.Empty)
        {
            return;
        }

        await gate.WaitAsync();
        try
        {
            if (isDisabled)
            {
                return;
            }

            if (containers.TryGetValue(containerId, out var context))
            {
                RemoveContainerContext(context);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetTransientStateAsync()
    {
        await gate.WaitAsync();
        try
        {
            pending.Clear();
            containers.Clear();
            itemLocations.Clear();
            vaultTabs.Clear();
            knownLegendaryObjects.Clear();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<List<LegendaryItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = new LocalContext();
        return await db.LegendaryItems
            .AsNoTracking()
            .Where(item => item.HasItemDetails && item.HasLegendaryDetails)
            .Include(item => item.Traits.OrderBy(trait => trait.Position))
            .OrderByDescending(item => item.LastSeenAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> DeleteItemsAsync(IEnumerable<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        var ids = itemIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return 0;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = new LocalContext();
            var items = await db.LegendaryItems
                .Where(item => ids.Contains(item.Id))
                .ToListAsync(cancellationToken);
            db.LegendaryItems.RemoveRange(items);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var item in items)
            {
                knownLegendaryObjects.Remove(item.ObjectId);
            }
            return items.Count;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        gate.Dispose();
    }

    private async void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableAwakeningItemsTracker))
        {
            return;
        }

        var disabled = settingsManager.UserSettings.DisableAwakeningItemsTracker;
        if (isDisabled == disabled)
        {
            return;
        }

        isDisabled = disabled;
        try
        {
            if (disabled)
            {
                await ResetTransientStateAsync();
            }
            Log.Information(disabled
                ? "Awakening items tracker disabled; transient tracking data was reset."
                : "Awakening items tracker enabled.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply the awakening items tracker setting");
        }
    }

    private PendingObservation GetPending(long objectId)
    {
        if (!pending.TryGetValue(objectId, out var observation))
        {
            observation = new PendingObservation();
            pending[objectId] = observation;
        }
        return observation;
    }

    private async Task UpsertAsync(long objectId, PendingObservation observation, DateTime seenAtUtc)
    {
        if (observation.Item is null || observation.Soul is null)
        {
            return;
        }
        var serverId = playerState.AlbionServer?.Id;
        if (serverId is null or <= 0)
        {
            Log.Debug("Skipping legendary item {ObjectId} because the Albion server is unknown", objectId);
            return;
        }

        await using var db = new LocalContext();
        var items = db.LegendaryItems.Include(candidate => candidate.Traits);
        var item = await items.FirstOrDefaultAsync(candidate =>
            candidate.AlbionServerId == serverId.Value
            && candidate.SoulId == observation.Soul.SoulId);
        item ??= await items.FirstOrDefaultAsync(candidate =>
            candidate.AlbionServerId == serverId.Value
            && candidate.ObjectId == objectId
            && candidate.SoulId == null);
        if (item is null)
        {
            item = new LegendaryItem
            {
                Id = Guid.NewGuid(),
                AlbionServerId = serverId.Value,
                ObjectId = objectId,
                FirstSeenAtUtc = seenAtUtc,
                LocationKind = LegendaryItemLocationKind.Unknown
            };
            db.LegendaryItems.Add(item);
        }

        var previousObjectId = item.ObjectId;
        var previousLastSeenAtUtc = item.LastSeenAtUtc;
        item.ObjectId = objectId;
        item.SeenByPlayerName = playerState.PlayerName;
        ApplyItem(item, observation.Item);
        ApplySoul(db, item, observation.Soul);
        ApplyAttunedToPlayerName(item, observation.Soul);
        if (itemLocations.TryGetValue(objectId, out var context))
        {
            ApplyLocation(item, context);
        }
        ApplyLocalAttunedToFallback(item);

        db.ChangeTracker.DetectChanges();
        var hasMeaningfulChanges = db.ChangeTracker.HasChanges();
        if (!hasMeaningfulChanges && !IsLastSeenUpdateDue(previousLastSeenAtUtc, seenAtUtc))
        {
            RememberObservedObject(objectId, previousObjectId);
            return;
        }
        item.LastSeenAtUtc = seenAtUtc;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save legendary item for server {ServerId}, object {ObjectId}", serverId, objectId);
            return;
        }

        RememberObservedObject(objectId, previousObjectId);
        Log.Debug(
            "Saved legendary item {LegendaryItemId} for server {ServerId}, soul {SoulId}, object {ObjectId} with {TraitCount} traits",
            item.Id,
            item.AlbionServerId,
            item.SoulId,
            item.ObjectId,
            item.Traits.Count);
        NotifyItemsChanged();
    }

    private static void ApplyItem(LegendaryItem target, NewItem source)
    {
        target.ItemIndex = source.ItemIndex;
        target.ItemUniqueName = source.ItemUniqueName;
        target.ItemName = source.ItemUsName;
        target.CrafterName = source.CrafterName;
        target.Quantity = source.Quantity;
        target.CurrentDurability = source.CurrentDurability;
        target.EstimatedMarketValue = source.EstimatedMarketValue;
        target.Quality = source.Quality;
        target.HasItemDetails = true;
    }

    private static void ApplySoul(LocalContext db, LegendaryItem target, LegendarySoul soul)
    {
        target.SoulId = soul.SoulId;
        if (!string.IsNullOrWhiteSpace(soul.SoulName))
        {
            target.SoulName = soul.SoulName;
        }
        target.Era = soul.Era;
        target.PvPFameGained = soul.PvPFameGained;
        target.AttunementSpent = soul.AttunementSpent;
        target.Attunement = soul.Attunement;
        target.Strain = soul.Strain;
        target.HasLegendaryDetails = true;
        var existingTraits = target.Traits.OrderBy(trait => trait.Position).ToArray();
        var traitsUnchanged = existingTraits.Length == soul.TraitsIds.Length
            && !existingTraits.Where((trait, index) =>
                !string.Equals(trait.TraitId, soul.TraitsIds[index], StringComparison.Ordinal)
                || trait.Value != soul.TraitsValues[index]).Any();
        if (traitsUnchanged)
        {
            return;
        }

        db.LegendaryItemTraits.RemoveRange(existingTraits);
        target.Traits.Clear();
        for (var index = 0; index < soul.TraitsIds.Length; index++)
        {
            target.Traits.Add(new LegendaryItemTrait
            {
                Id = Guid.NewGuid(),
                LegendaryItemId = target.Id,
                Position = index,
                TraitId = soul.TraitsIds[index],
                Value = soul.TraitsValues[index]
            });
        }
    }

    private void ApplyAttunedToPlayerName(LegendaryItem target, LegendarySoul soul)
    {
        if (soul.AttunedToMe && !string.IsNullOrWhiteSpace(playerState.PlayerName))
        {
            target.AttunedToPlayerName = playerState.PlayerName;
        }
        else if (!string.IsNullOrWhiteSpace(soul.AttunedToPlayerName))
        {
            target.AttunedToPlayerName = soul.AttunedToPlayerName;
        }
    }

    private ContainerContext CreateContainerContext(
        long objectId,
        Guid containerId,
        Guid privateContainerId,
        IReadOnlyList<long> slotItems)
    {
        var context = new ContainerContext
        {
            ObjectId = objectId > 0 ? objectId : null,
            ContainerId = containerId,
            PrivateContainerId = privateContainerId,
            SlotItems = slotItems.ToArray(),
            RawLocationId = playerState.Location.Id,
            LocationName = playerState.Location.FriendlyName,
            Kind = objectId == playerState.UserObjectId
                ? LegendaryItemLocationKind.Inventory
                : LegendaryItemLocationKind.Container,
            ContainerName = objectId == playerState.UserObjectId ? "Inventory" : "Container"
        };
        if (privateContainerId != Guid.Empty && vaultTabs.TryGetValue(privateContainerId, out var metadata))
        {
            ApplyMetadata(context, metadata);
        }
        return context;
    }

    private void RemoveContainerContext(ContainerContext context)
    {
        foreach (var key in containers
            .Where(entry => ReferenceEquals(entry.Value, context))
            .Select(entry => entry.Key)
            .ToArray())
        {
            containers.Remove(key);
        }
        foreach (var objectId in itemLocations
            .Where(entry => ReferenceEquals(entry.Value, context))
            .Select(entry => entry.Key)
            .ToArray())
        {
            itemLocations.Remove(objectId);
        }
    }

    private static void ApplyMetadata(ContainerContext context, VaultTabMetadata metadata)
    {
        context.Kind = metadata.Kind;
        context.ObjectId = metadata.ObjectId ?? context.ObjectId;
        context.RawLocationId = metadata.RawLocationId;
        context.LocationName = metadata.LocationName;
        context.ContainerName = metadata.Name;
        context.ContainerIcon = metadata.Icon;
        context.ContainerColor = metadata.Color;
    }

    private static void ApplyLocation(LegendaryItem item, ContainerContext context)
    {
        item.LocationKind = context.Kind;
        item.RawLocationId = context.RawLocationId;
        item.LocationName = context.LocationName;
        item.ContainerObjectId = context.ObjectId;
        item.ContainerId = context.ContainerId;
        item.PrivateContainerId = context.PrivateContainerId == Guid.Empty ? null : context.PrivateContainerId;
        item.ContainerName = context.ContainerName;
        item.ContainerIcon = context.ContainerIcon;
        item.ContainerColor = context.ContainerColor;
    }

    private async Task ApplyContainerToStoredItemsAsync(
        ContainerContext context,
        IReadOnlyList<long> objectIds,
        DateTime seenAtUtc)
    {
        var serverId = playerState.AlbionServer?.Id;
        var ids = objectIds.Where(value => value > 0).Distinct().ToArray();
        if (serverId is null or <= 0 || ids.Length == 0)
        {
            return;
        }

        await using var db = new LocalContext();
        var items = await db.LegendaryItems
            .Where(item => item.AlbionServerId == serverId.Value && ids.Contains(item.ObjectId))
            .ToListAsync();
        foreach (var item in items)
        {
            knownLegendaryObjects.Add(item.ObjectId);
            item.SeenByPlayerName = playerState.PlayerName;
            ApplyLocation(item, context);
            ApplyLocalAttunedToFallback(item);
        }
        if (items.Count == 0)
        {
            return;
        }

        db.ChangeTracker.DetectChanges();
        foreach (var item in items)
        {
            if (db.Entry(item).State == EntityState.Modified
                || IsLastSeenUpdateDue(item.LastSeenAtUtc, seenAtUtc))
            {
                item.LastSeenAtUtc = seenAtUtc;
            }
        }
        if (!db.ChangeTracker.HasChanges())
        {
            return;
        }

        await db.SaveChangesAsync();
        NotifyItemsChanged();
    }

    private void ApplyLocalAttunedToFallback(LegendaryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.AttunedToPlayerName)
            || item.Attunement is null or <= 0
            || item.LocationKind is not (LegendaryItemLocationKind.Inventory or LegendaryItemLocationKind.Bank)
            || string.IsNullOrWhiteSpace(playerState.PlayerName))
        {
            return;
        }

        item.AttunedToPlayerName = playerState.PlayerName;
    }

    private async Task TouchStoredItemAsync(long objectId, DateTime seenAtUtc)
    {
        var serverId = playerState.AlbionServer?.Id;
        if (serverId is null or <= 0)
        {
            return;
        }

        await using var db = new LocalContext();
        var item = await db.LegendaryItems.FirstOrDefaultAsync(candidate =>
            candidate.AlbionServerId == serverId.Value && candidate.ObjectId == objectId);
        if (item is null)
        {
            return;
        }
        item.SeenByPlayerName = playerState.PlayerName;
        db.ChangeTracker.DetectChanges();
        if (db.Entry(item).State != EntityState.Modified
            && !IsLastSeenUpdateDue(item.LastSeenAtUtc, seenAtUtc))
        {
            return;
        }
        item.LastSeenAtUtc = seenAtUtc;
        await db.SaveChangesAsync();
        NotifyItemsChanged();
    }

    private void RememberObservedObject(long objectId, long previousObjectId)
    {
        pending.Remove(objectId);
        if (previousObjectId != objectId)
        {
            knownLegendaryObjects.Remove(previousObjectId);
        }
        knownLegendaryObjects.Add(objectId);
    }

    private static bool IsLastSeenUpdateDue(DateTime previous, DateTime current)
    {
        return previous == default || current - previous >= LastSeenUpdateInterval;
    }

    private void NotifyItemsChanged()
    {
        try
        {
            ItemsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Legendary item observer failed");
        }
    }

    private sealed class PendingObservation
    {
        public NewItem? Item { get; set; }
        public LegendarySoul? Soul { get; set; }
    }

    private sealed class ContainerContext
    {
        public long? ObjectId { get; set; }
        public Guid ContainerId { get; set; }
        public Guid PrivateContainerId { get; set; }
        public IReadOnlyList<long> SlotItems { get; set; } = Array.Empty<long>();
        public LegendaryItemLocationKind Kind { get; set; }
        public string RawLocationId { get; set; } = string.Empty;
        public string LocationName { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string ContainerIcon { get; set; } = string.Empty;
        public int? ContainerColor { get; set; }
    }

    private sealed record VaultTabMetadata(
        LegendaryItemLocationKind Kind,
        long? ObjectId,
        string RawLocationId,
        string LocationName,
        string Name,
        string Icon,
        int? Color);
}
