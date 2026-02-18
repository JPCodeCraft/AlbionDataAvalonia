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
    public const EventCodes ProbeEventCode = EventCodes.LaborerGotUpgraded;

    public DebugEventProbeEventHandler() : base((int)ProbeEventCode)
    {
    }

    protected override Task OnActionAsync(DebugEventProbeEvent value)
    {
        Log.Information(
            "Debug probe captured event {EventCode} ({EventName}) with {ParameterCount} parameter(s).",
            (int)ProbeEventCode,
            ProbeEventCode,
            value.Parameters.Count);

        foreach (var parameter in value.Parameters.OrderBy(x => x.Key))
        {
            Log.Information(
                "Debug probe param key={Key} type={Type} value={@Value}",
                parameter.Key,
                parameter.Value?.GetType().FullName ?? "null",
                parameter.Value);
        }

        return Task.CompletedTask;
    }
}
