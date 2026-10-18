using Albion.Network;
using AlbionDataAvalonia.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Lets diagnostics mode record the packets that may say which layout a Mists or Ancient Lands instance uses
/// and what the server placed in a map's dynamic slots. Diagnostics only writes packets that have a handler,
/// so these claim them while diagnostics is on and do nothing with them.
/// </summary>
internal static partial class MapSlotDiagnostics
{
    public static void Register(ReceiverBuilder builder, Func<bool> isEnabled)
    {
        builder.AddHandler(new EventHandler(isEnabled));
        // Entering the Mists through a wisp sends no cluster change, so its layout must arrive in some
        // other, unhandled response; record every response until it is found.
        builder.AddHandler(new ResponseHandler(isEnabled));
    }

    private static readonly HashSet<short> InstanceEventCodes = Enum.GetValues<EventCodes>()
        .Where(code => code is EventCodes.DynamicTemplate or EventCodes.DynamicTemplateForcedStateChange
            || code.ToString().Contains("Mists", StringComparison.Ordinal) || code.ToString().Contains("DragonArea", StringComparison.Ordinal))
        .Select(code => (short)code).ToHashSet();

    // Slot pieces are named like MISTS_S_PVE_HER_CAMP_01, M_DR_Mobcamp_01 or S_HL_DynamicResource_Wood_01.
    [GeneratedRegex(@"^(MISTS_|DRAGON_|[SML]_[A-Z]{2}_)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SlotTemplateName();

    private static bool NamesSlotTemplate(Dictionary<byte, object> parameters)
    {
        foreach (var value in parameters.Values)
        {
            if (value is string text && SlotTemplateName().IsMatch(text)) return true;
            // Only text arrays can name a template; numeric arrays are skipped without boxing.
            if (value is string[] or object[])
                foreach (var item in (Array)value)
                    if (item is string itemText && SlotTemplateName().IsMatch(itemText)) return true;
        }

        return false;
    }

    private sealed class EventHandler(Func<bool> isEnabled) : PacketHandler<EventPacket>
    {
        public override bool CanHandle(object packet) => isEnabled() && packet is EventPacket value
            && (InstanceEventCodes.Contains(value.EventCode) || NamesSlotTemplate(value.Parameters));

        protected override Task OnHandleAsync(EventPacket packet) => Task.CompletedTask;
    }

    private sealed class ResponseHandler(Func<bool> isEnabled) : PacketHandler<ResponsePacket>
    {
        public override bool CanHandle(object packet) => isEnabled() && packet is ResponsePacket;

        protected override Task OnHandleAsync(ResponsePacket packet) => Task.CompletedTask;
    }
}
