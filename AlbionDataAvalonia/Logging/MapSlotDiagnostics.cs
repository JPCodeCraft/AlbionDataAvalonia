using Albion.Network;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Lets diagnostics mode record every event and response, to find which packets say what the server placed
/// in a map's dynamic slots. Diagnostics only writes packets that have a handler, so these claim them while
/// diagnostics is on and do nothing with them.
/// </summary>
internal static class MapSlotDiagnostics
{
    public static void Register(ReceiverBuilder builder, Func<bool> isEnabled)
    {
        builder.AddHandler(new EventHandler(isEnabled));
        // Entering the Mists through a wisp sends no cluster change, so its layout must arrive in some
        // other, unhandled response; record every response until it is found.
        builder.AddHandler(new ResponseHandler(isEnabled));
    }

    // The open-world pieces drawn in a zone's random squares are not named by any known packet,
    // so every event is recorded until the one carrying them is found.
    private sealed class EventHandler(Func<bool> isEnabled) : PacketHandler<EventPacket>
    {
        public override bool CanHandle(object packet) => isEnabled() && packet is EventPacket;

        protected override Task OnHandleAsync(EventPacket packet) => Task.CompletedTask;
    }

    private sealed class ResponseHandler(Func<bool> isEnabled) : PacketHandler<ResponsePacket>
    {
        public override bool CanHandle(object packet) => isEnabled() && packet is ResponsePacket;

        protected override Task OnHandleAsync(ResponsePacket packet) => Task.CompletedTask;
    }
}
