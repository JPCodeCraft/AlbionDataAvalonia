using Albion.Network;
using AlbionDataAvalonia.Shared;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Lets diagnostics mode record the events that may say which template the server placed in a map's
/// dynamic slots (Mists, Ancient Lands and open-world camps). Diagnostics only writes packets that have
/// a handler, so this claims them while diagnostics is on and does nothing with them.
/// </summary>
internal sealed partial class MapSlotDiagnosticsHandler(Func<bool> isEnabled) : PacketHandler<EventPacket>
{
    public override bool CanHandle(object packet) => isEnabled() && packet is EventPacket value
        && (value.EventCode is (short)EventCodes.DynamicTemplate or (short)EventCodes.DynamicTemplateForcedStateChange
            || NamesSlotTemplate(value.Parameters));

    protected override Task OnHandleAsync(EventPacket packet) => Task.CompletedTask;

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
}
