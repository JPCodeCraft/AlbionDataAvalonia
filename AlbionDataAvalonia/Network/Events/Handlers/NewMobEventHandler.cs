using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewMobEventHandler : EventPacketHandler<NewMobEvent>
{
    private readonly CombatTrackerService combatTracker;
    private readonly MobsService mobsService;

    public NewMobEventHandler(CombatTrackerService combatTracker, MobsService mobsService) : base((int)EventCodes.NewMob)
    {
        this.combatTracker = combatTracker;
        this.mobsService = mobsService;
    }

    protected override Task OnActionAsync(NewMobEvent value)
    {
        if (value.ObjectId is { } objectId)
        {
            combatTracker.AddOrUpdateMob(objectId, value.MobIndex, mobsService.GetMobName(value.MobIndex));
        }

        return Task.CompletedTask;
    }
}
