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
        (int)EventCodes.EstimatedMarketValueUpdate,
        (int)EventCodes.NewSimpleItem,
        (int)EventCodes.NewJournalItem,
        (int)EventCodes.NewLaborerItem,
        (int)EventCodes.NewEquipmentItem,
        (int)EventCodes.NewFurnitureItem,
        (int)EventCodes.NewKillTrophyItem,
        (int)EventCodes.NewSiegeBannerItem
    ];

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
