using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class JoinRequestHandler(FarmingTrackerService tracker) : RequestPacketHandler<JoinRequest>((int)OperationCodes.Join)
{
    protected override Task OnActionAsync(JoinRequest value)
    {
        tracker.OnJoinStarted();
        return Task.CompletedTask;
    }
}
