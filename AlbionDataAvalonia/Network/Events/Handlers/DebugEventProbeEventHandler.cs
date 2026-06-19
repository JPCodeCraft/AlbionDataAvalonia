using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugEventProbeEventHandler : EventPacketHandler<DebugEventProbeEvent>
{
    private static readonly EventCodes[] ProbeEventCodes =
    [
        EventCodes.NewLoot,
        EventCodes.InventoryPutItem,
        EventCodes.AttachItemContainer,
        EventCodes.DetachItemContainer,
        EventCodes.InvalidateItemContainer,
        EventCodes.LockItemContainer,
        EventCodes.InventoryDeleteItem,
        EventCodes.InventoryState,
        EventCodes.CharacterStats,
        EventCodes.LootEquipmentChanged,
        EventCodes.PartyJoined,
        EventCodes.PartyDisbanded,
        EventCodes.PartyPlayerJoined,
        EventCodes.PartyPlayerLeft,
        EventCodes.PartyLootSettingChangedPlayer,
        EventCodes.PartyPlayerUpdated,
        EventCodes.PartyInvitationAnswer,
        EventCodes.PartyMarkedObjectsUpdated,
        EventCodes.PartyOnClusterPartyJoined,
        EventCodes.PartySetRoleFlag,
        EventCodes.OtherGrabbedLoot,
        EventCodes.TreasureChestUsingStart,
        EventCodes.TreasureChestUsingFinished,
        EventCodes.TreasureChestUsingCancel,
        EventCodes.TreasureChestUsingOpeningComplete,
        EventCodes.TreasureChestForceCloseInventory,
        EventCodes.LocalTreasuresUpdate,
        EventCodes.PartyLootItems,
        EventCodes.PartyLootItemsRemoved,
        EventCodes.PartyLootItemTypesRemoved,
        EventCodes.NewLootChest,
        EventCodes.UpdateLootChest,
        EventCodes.LootChestOpened,
        EventCodes.UpdateLootProtectedByMobsWithMinimapDisplay
    ];

    private static readonly int[] ProbeEventCodeValues = ProbeEventCodes
        .Select(code => (int)code)
        .ToArray();

    public DebugEventProbeEventHandler() : base(ProbeEventCodeValues)
    {
    }

    protected override Task OnHandleAsync(EventPacket packet)
    {
        if (!ProbeEventCodeValues.Contains(packet.EventCode))
        {
            return NextAsync(packet);
        }

        var value = new DebugEventProbeEvent(packet.Parameters);
        Log.Debug(
            "Debug probe captured event {EventCode} ({EventName}) with {ParameterCount} parameter(s): {Parameters}",
            packet.EventCode,
            GetEventName(packet.EventCode),
            value.Parameters.Count,
            DebugProbeFormatter.FormatParameters(value.Parameters));

        return NextAsync(packet);
    }

    private static string GetEventName(int eventCode)
    {
        var eventName = ProbeEventCodes
            .FirstOrDefault(code => (int)code == eventCode);

        return (int)eventName == eventCode
            ? eventName.ToString()
            : "Unknown";
    }

    protected override Task OnActionAsync(DebugEventProbeEvent value)
    {
        return Task.CompletedTask;
    }
}
