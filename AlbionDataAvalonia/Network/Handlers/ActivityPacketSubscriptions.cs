using Albion.Network;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

internal static class ActivityPacketSubscriptions
{
    // Register after shared handlers, party/identity updates, and item enrichment.
    // Observers enqueue immutable journal mutations; they never wait for SQLite.
    public static void Register(ReceiverBuilder builder, ActivityTrackerService activity)
    {
        Event<NewCharacterEvent>(builder, EventCodes.NewCharacter, activity);
        Event<NewMobEvent>(builder, EventCodes.NewMob, activity);
        Event<LeaveEvent>(builder, EventCodes.Leave, activity);
        Event<NewRandomDungeonExitEvent>(builder, EventCodes.NewRandomDungeonExit, activity);
        Event<HealthUpdateEvent>(builder, EventCodes.HealthUpdate, activity);
        Event<HealthUpdatesEvent>(builder, EventCodes.HealthUpdates, activity);
        Event<UpdateFameEvent>(builder, EventCodes.UpdateFame, activity);
        Event<AchievementProgressInfoEvent>(builder, EventCodes.AchievementProgressInfo, activity);
        Event<TakeSilverEvent>(builder, EventCodes.TakeSilver, activity);
        Event<HarvestFinishedEvent>(builder, EventCodes.HarvestFinished, activity);
        Event<RewardGrantedEvent>(builder, EventCodes.RewardGranted, activity);
        Event<JournalGotFullEvent>(builder, EventCodes.JournalGotFull, activity);
        Event<DiedEvent>(builder, EventCodes.Died, activity);
        Event<KnockedDownEvent>(builder, EventCodes.KnockedDown, activity);
        Event<KilledPlayerEvent>(builder, EventCodes.KilledPlayer, activity);
        Event<PartySilverGainedEvent>(builder, EventCodes.PartySilverGained, activity);
        Event<PartyJoinedEvent>(builder, EventCodes.PartyJoined, activity);
        Event<PartyPlayerJoinedEvent>(builder, EventCodes.PartyPlayerJoined, activity);
        Event<PartyPlayerLeftEvent>(builder, EventCodes.PartyPlayerLeft, activity);
        Event<PartyDisbandedEvent>(builder, EventCodes.PartyDisbanded, activity);
        Event<PartyOnClusterPartyJoinedEvent>(builder, EventCodes.PartyOnClusterPartyJoined, activity);
        Event<PartySetRoleFlagEvent>(builder, EventCodes.PartySetRoleFlag, activity);
        Event<NewLootEvent>(builder, EventCodes.NewLoot, activity);
        Event<NewLootChestEvent>(builder, EventCodes.NewLootChest, activity);
        Event<UpdateLootChestEvent>(builder, EventCodes.UpdateLootChest, activity);
        Event<LootChestOpenedEvent>(builder, EventCodes.LootChestOpened, activity);
        Event<AttachItemContainerEvent>(builder, EventCodes.AttachItemContainer, activity);
        Event<DetachItemContainerEvent>(builder, EventCodes.DetachItemContainer, activity);
        Event<InventoryStateEvent>(builder, EventCodes.InventoryState, activity);
        Event<InventoryPutItemEvent>(builder, EventCodes.InventoryPutItem, activity);
        Event<InventoryDeleteItemEvent>(builder, EventCodes.InventoryDeleteItem, activity);
        Event<OtherGrabbedLootEvent>(builder, EventCodes.OtherGrabbedLoot, activity);
        Event<PartyLootItemsEvent>(builder, EventCodes.PartyLootItems, activity);
        Event<PartyLootItemsRemovedEvent>(builder, EventCodes.PartyLootItemsRemoved, activity);
        Event<PartyLootItemTypesRemovedEvent>(builder, EventCodes.PartyLootItemTypesRemoved, activity);
        Event<NewSimpleItemEvent>(builder, EventCodes.NewSimpleItem, activity);
        Event<NewEquipmentItemEvent>(builder, EventCodes.NewEquipmentItem, activity);
        Event<NewFurnitureItemEvent>(builder, EventCodes.NewFurnitureItem, activity);
        Event<NewJournalItemEvent>(builder, EventCodes.NewJournalItem, activity);
        Event<NewLaborerItemEvent>(builder, EventCodes.NewLaborerItem, activity);
        Event<NewKillTrophyItemEvent>(builder, EventCodes.NewKillTrophyItem, activity);
        Event<NewSiegeBannerItemEvent>(builder, EventCodes.NewSiegeBannerItem, activity);
        builder.SubscribeRequest<JoinRequest>((int)OperationCodes.Join, packet => Observe(activity, packet));
        builder.SubscribeResponse<JoinResponse>((int)OperationCodes.Join, packet => Observe(activity, packet));
        builder.SubscribeResponse<ChangeClusterResponse>((int)OperationCodes.ChangeCluster, packet =>
        {
            // The layout names a Mists instance's minimap; logged until its delivery is confirmed in game.
            Log.Debug("Cluster change to {Destination} with layout {LayoutId}, return code {ReturnCode}",
                packet.Destination, packet.LayoutId, packet.ReturnCode);
            return Observe(activity, packet);
        });
        builder.SubscribeRequest<ExitEnterStartRequest>((int)OperationCodes.ExitEnterStart, packet => Observe(activity, packet));
        builder.SubscribeRequest<InventoryMoveItemRequest>((int)OperationCodes.InventoryMoveItem, packet => Observe(activity, packet));
        builder.SubscribeRequest<InventoryMoveGivenItemsRequest>((int)OperationCodes.InventoryMoveGivenItems, packet => Observe(activity, packet));
        builder.SubscribeRequest<FishingStartRequest>((int)OperationCodes.FishingStart, packet => Observe(activity, packet));
        builder.SubscribeRequest<FishingFinishRequest>((int)OperationCodes.FishingFinish, packet => Observe(activity, packet));
        builder.SubscribeRequest<FishingCancelRequest>((int)OperationCodes.FishingCancel, packet => Observe(activity, packet));
        builder.SubscribeResponse<FishingFinishResponse>((int)OperationCodes.FishingFinish, packet => Observe(activity, packet));
        builder.SubscribeRequest<MoveRequest>((int)OperationCodes.Move, packet => { activity.ObserveMove(packet); return Task.CompletedTask; });
        // Wisp spawns and border exits stand at fixed points of a Mists layout and place it.
        builder.SubscribeEvent<MistsLandmarkEvent>((int)EventCodes.NewMistsWispSpawn, packet => { activity.ObserveMistsLandmark(packet); return Task.CompletedTask; });
        builder.SubscribeEvent<MistsLandmarkEvent>((int)EventCodes.NewMistsBorderExit, packet => { activity.ObserveMistsLandmark(packet); return Task.CompletedTask; });
        builder.AddHandler(new AcquisitionIntentHandler(activity));
    }

    private static void Event<T>(ReceiverBuilder builder, EventCodes code, ActivityTrackerService activity) where T : BaseEvent
        => builder.SubscribeEvent<T>((int)code, packet => { activity.Observe(packet); return Task.CompletedTask; });

    private static Task Observe(ActivityTrackerService activity, BaseOperation packet)
    {
        activity.Observe(packet);
        return Task.CompletedTask;
    }

    // These requests do not prove acquisitions. They only make nearby stack
    // gains ambiguous, so discard the short-lived fishing correlation.
    private sealed class AcquisitionIntentHandler(ActivityTrackerService activity) : PacketHandler<RequestPacket>
    {
        protected override Task OnHandleAsync(RequestPacket packet)
        {
            if (packet.OperationCode == (int)OperationCodes.ExitEnterCancel)
                activity.CancelEntranceSelection(packet);
            if (packet.OperationCode is (int)OperationCodes.InventorySplitStack or (int)OperationCodes.InventorySplitStackInto
                or (int)OperationCodes.InventoryStack or (int)OperationCodes.InventoryAddToStacks
                or (int)OperationCodes.InventoryDestroyItem or (int)OperationCodes.InventoryDropAll)
                activity.ObserveInventoryRearrangement(packet);
            return Task.CompletedTask;
        }
    }
}
