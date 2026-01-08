using Albion.Network;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class CharacterStatsEventHandler : EventPacketHandler<CharacterStatsEvent>
{
    public CharacterStatsEventHandler() : base((int)EventCodes.CharacterStats)
    {
    }

    protected override async Task OnActionAsync(CharacterStatsEvent value)
    {
        await Task.CompletedTask;
    }
}
