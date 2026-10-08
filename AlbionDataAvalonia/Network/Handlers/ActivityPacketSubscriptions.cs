using Albion.Network;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
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
        Event<HealthUpdateEvent>(builder, EventCodes.HealthUpdate, activity);
        Event<HealthUpdatesEvent>(builder, EventCodes.HealthUpdates, activity);
        Event<UpdateFameEvent>(builder, EventCodes.UpdateFame, activity);
        Event<TakeSilverEvent>(builder, EventCodes.TakeSilver, activity);
        Event<DiedEvent>(builder, EventCodes.Died, activity);
        Event<KnockedDownEvent>(builder, EventCodes.KnockedDown, activity);
        Event<KilledPlayerEvent>(builder, EventCodes.KilledPlayer, activity);
        Event<PartySilverGainedEvent>(builder, EventCodes.PartySilverGained, activity);
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
        builder.SubscribeRequest<InventoryMoveItemRequest>((int)OperationCodes.InventoryMoveItem, packet => Observe(activity, packet));
        builder.SubscribeRequest<InventoryMoveGivenItemsRequest>((int)OperationCodes.InventoryMoveGivenItems, packet => Observe(activity, packet));
    }

    private static void Event<T>(ReceiverBuilder builder, EventCodes code, ActivityTrackerService activity) where T : BaseEvent
        => builder.SubscribeEvent<T>((int)code, packet => { activity.Observe(packet); return Task.CompletedTask; });

    private static Task Observe(ActivityTrackerService activity, BaseOperation packet)
    {
        activity.Observe(packet);
        return Task.CompletedTask;
    }
}
