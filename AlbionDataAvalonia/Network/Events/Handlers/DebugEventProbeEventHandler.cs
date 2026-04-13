using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class DebugEventProbeEventHandler : EventPacketHandler<DebugEventProbeEvent>
{
    // Change this single constant to probe a different event.
    public const EventCodes ProbeEventCode = EventCodes.UpdateChatSettings;

    public DebugEventProbeEventHandler() : base((int)ProbeEventCode)
    {
    }

    protected override Task OnHandleAsync(EventPacket packet)
    {
        if (packet.EventCode != (int)ProbeEventCode)
        {
            return NextAsync(packet);
        }

        var value = new DebugEventProbeEvent(packet.Parameters);
        Log.Debug(
            "Debug probe captured event {EventCode} ({EventName}) with {ParameterCount} parameter(s).",
            (int)ProbeEventCode,
            ProbeEventCode,
            value.Parameters.Count);

        foreach (var parameter in value.Parameters.OrderBy(x => x.Key))
        {
            Log.Debug(
                "Debug probe param key={Key} type={Type} value={@Value}",
                parameter.Key,
                parameter.Value?.GetType().FullName ?? "null",
                parameter.Value);
        }

        return NextAsync(packet);
    }

    protected override Task OnActionAsync(DebugEventProbeEvent value)
    {
        return Task.CompletedTask;
    }
}
