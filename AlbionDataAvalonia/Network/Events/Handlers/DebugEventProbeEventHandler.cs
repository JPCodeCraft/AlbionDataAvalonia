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
    private static readonly int[] ProbeEventCodeValues =
    [
        (int)EventCodes.PremiumChanged,
        (int)EventCodes.JoinFinished,
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
        (int)EventCodes.TimeSync,
    ];

    public DebugEventProbeEventHandler() : base(ProbeEventCodeValues)
    {
    }

    protected override Task OnHandleAsync(EventPacket packet)
    {
        if (!Log.IsEnabled(Serilog.Events.LogEventLevel.Debug) || !ProbeEventCodeValues.Contains(packet.EventCode))
        {
            return NextAsync(packet);
        }
        var value = new DebugEventProbeEvent(packet.Parameters);
        Log.Debug(
            "Debug probe captured event {EventCode} ({EventName}) with {ParameterCount} parameter(s): {Parameters}",
            packet.EventCode,
            System.Enum.GetName(typeof(EventCodes), packet.EventCode) ?? "Unknown",
            value.Parameters.Count,
            DebugProbeFormatter.FormatParameters(value.Parameters));

        return NextAsync(packet);
    }

    protected override Task OnActionAsync(DebugEventProbeEvent value)
    {
        return Task.CompletedTask;
    }
}
