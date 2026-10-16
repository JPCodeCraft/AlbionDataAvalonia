using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugEventProbeEventHandler : EventPacketHandler<DebugEventProbeEvent>
{
    private readonly Func<bool> isEnabled;

    private static readonly int[] ProbeEventCodeValues =
    [
        (int)EventCodes.PremiumChanged,
        (int)EventCodes.JoinFinished,
        // Activity identity, combat, earnings, and party snapshots.
        (int)EventCodes.NewCharacter,
        (int)EventCodes.NewMob,
        (int)EventCodes.MobChangeState,
        (int)EventCodes.HealthUpdate,
        (int)EventCodes.HealthUpdates,
        (int)EventCodes.InCombatStateUpdate,
        (int)EventCodes.RegenerationHealthChanged,
        (int)EventCodes.RegenerationHealthEnergyComboChanged,
        (int)EventCodes.UpdateFame,
        // Keep source/progression fields raw until fame categories are verified.
        (int)EventCodes.FullAchievementInfo,
        (int)EventCodes.FinishedAchievement,
        (int)EventCodes.AchievementProgressInfo,
        (int)EventCodes.FullAchievementProgressInfo,
        (int)EventCodes.JournalAchievementProgressUpdate,
        // PvP fame mappings remain unverified; retain possible kill/stat rewards for inspection.
        (int)EventCodes.CharacterStats,
        (int)EventCodes.CharacterStatsKillHistory,
        (int)EventCodes.CharacterStatsDeathHistory,
        (int)EventCodes.KillHistoryDetails,
        (int)EventCodes.ItemKillHistoryDetails,
        (int)EventCodes.KillRewardedNoFame,
        (int)EventCodes.EpicAchievementAndStatsUpdate,
        // Resource identity and the complete gather/catch lifecycle.
        (int)EventCodes.NewSimpleHarvestableObject,
        (int)EventCodes.NewSimpleHarvestableObjectList,
        (int)EventCodes.NewHarvestableObject,
        (int)EventCodes.HarvestableChangeState,
        (int)EventCodes.HarvestStart,
        (int)EventCodes.HarvestCancel,
        (int)EventCodes.HarvestFinished,
        (int)EventCodes.FishingStart,
        (int)EventCodes.FishingCast,
        (int)EventCodes.FishingCatch,
        (int)EventCodes.FishingFinished,
        (int)EventCodes.FishingCancel,
        (int)EventCodes.RewardGranted,
        // Journal fills and gathering bonus drops (tomes) are not decoded yet; capture their shape.
        (int)EventCodes.JournalGotFull,
        (int)EventCodes.JournalFillError,
        (int)EventCodes.NewMultiRewardObject,
        (int)EventCodes.TakeSilver,
        (int)EventCodes.NewSilverObject,
        (int)EventCodes.RemoveSilver,
        (int)EventCodes.UpdateMoney,
        (int)EventCodes.PartySilverGained,
        (int)EventCodes.Died,
        (int)EventCodes.KnockedDown,
        (int)EventCodes.KilledPlayer,
        (int)EventCodes.Respawn,
        (int)EventCodes.Teleport,
        (int)EventCodes.ClusterInfoUpdate,
        (int)EventCodes.NewRandomDungeonExit,
        (int)EventCodes.PartyJoined,
        (int)EventCodes.PartyPlayerJoined,
        (int)EventCodes.PartyPlayerLeft,
        (int)EventCodes.PartyDisbanded,
        (int)EventCodes.PartyOnClusterPartyJoined,
        (int)EventCodes.PartySetRoleFlag,
        (int)EventCodes.PartyPlayerUpdated,
        (int)EventCodes.PartyLootSettingChangedPlayer,
        // Preserve the full chest/inventory chain even if typed decoding fails.
        (int)EventCodes.NewLoot,
        (int)EventCodes.NewLootChest,
        (int)EventCodes.UpdateLootChest,
        (int)EventCodes.LootChestOpened,
        (int)EventCodes.AttachItemContainer,
        (int)EventCodes.DetachItemContainer,
        (int)EventCodes.InvalidateItemContainer,
        (int)EventCodes.LockItemContainer,
        (int)EventCodes.OtherGrabbedLoot,
        (int)EventCodes.PartyLootItems,
        (int)EventCodes.PartyLootItemsRemoved,
        (int)EventCodes.PartyLootItemTypesRemoved,
        (int)EventCodes.TreasureChestForceCloseInventory,
        (int)EventCodes.MatchLootChestOpeningStart,
        (int)EventCodes.MatchLootChestOpeningFinished,
        (int)EventCodes.MatchLootChestOpeningCancel,
        (int)EventCodes.BankVaultInfo,
        (int)EventCodes.GuildVaultInfo,
        // Candidate demolition/rebuild signals. Leave alone does not prove removal.
        (int)EventCodes.Leave,
        (int)EventCodes.AttackBuilding,
        (int)EventCodes.ActionOnBuildingStart,
        (int)EventCodes.ActionOnBuildingCancel,
        (int)EventCodes.ActionOnBuildingFinished,
        (int)EventCodes.ConstructionSiteInfo,
        (int)EventCodes.NewBuildingBaseEvent,
        (int)EventCodes.BuildingDurabilityUpdate,
        (int)EventCodes.MiniMapOwnedBuildingsPositions,
        (int)EventCodes.NewBuilding,
        (int)EventCodes.PlayerBuildingInfo,
        (int)EventCodes.FarmBuildingInfo,
        (int)EventCodes.FarmableObjectInfo,
        (int)EventCodes.PlaceableObjectPlace,
        (int)EventCodes.PlaceableObjectPlaceCancel,
        (int)EventCodes.BoostFarmable,
        (int)EventCodes.CraftingFocusUpdate,
        // Verify item identity and stack changes around placement and pickup.
        (int)EventCodes.NewSimpleItem,
        (int)EventCodes.NewEquipmentItem,
        (int)EventCodes.NewEquipmentItemLegendarySoul,
        (int)EventCodes.NewFurnitureItem,
        (int)EventCodes.NewJournalItem,
        (int)EventCodes.NewLaborerItem,
        (int)EventCodes.NewKillTrophyItem,
        (int)EventCodes.NewSiegeBannerItem,
        (int)EventCodes.EstimatedMarketValueUpdate,
        (int)EventCodes.InventoryPutItem,
        (int)EventCodes.InventoryDeleteItem,
        (int)EventCodes.InventoryState,
        (int)EventCodes.TimeSync,
    ];

    public DebugEventProbeEventHandler(Func<bool> isEnabled) : base(ProbeEventCodeValues)
    {
        this.isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
    }

    protected override Task OnHandleAsync(EventPacket packet)
    {
        if (!isEnabled() || !Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) || !ProbeEventCodeValues.Contains(packet.EventCode))
        {
            return Task.CompletedTask;
        }
        var value = new DebugEventProbeEvent(packet.Parameters);
        Log.Debug(
            "Debug probe captured event {EventCode} ({EventName}) with {ParameterCount} parameter(s). CapturedAt={CapturedAt:O}, ConnectionId={ConnectionId}, OriginKey={OriginKey}: {Parameters}",
            packet.EventCode,
            System.Enum.GetName(typeof(EventCodes), packet.EventCode) ?? "Unknown",
            value.Parameters.Count,
            packet.CapturedAt,
            packet.ConnectionId,
            packet.MessageIdentity?.OriginKey,
            DebugProbeFormatter.FormatParameters(value.Parameters));

        return Task.CompletedTask;
    }

    protected override Task OnActionAsync(DebugEventProbeEvent value)
    {
        return Task.CompletedTask;
    }
}
