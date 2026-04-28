using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class TimeSyncEventHandler : EventPacketHandler<TimeSyncEvent>
{
    private readonly CombatTrackerService combatTracker;

    public TimeSyncEventHandler(CombatTrackerService combatTracker) : base((int)EventCodes.TimeSync)
    {
        this.combatTracker = combatTracker;
    }

    protected override Task OnActionAsync(TimeSyncEvent value)
    {
        if (value.GameTimeMilliseconds is { } gameTimeMilliseconds)
        {
            combatTracker.UpdateGameTimeAnchorFromTimeSync(gameTimeMilliseconds, DateTime.UtcNow);
        }

        return Task.CompletedTask;
    }
}
