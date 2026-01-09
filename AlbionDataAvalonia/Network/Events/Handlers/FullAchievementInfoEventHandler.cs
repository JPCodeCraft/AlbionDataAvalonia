using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Serilog;

namespace AlbionDataAvalonia.Network.Handlers;

public class FullAchievementInfoEventHandler : EventPacketHandler<FullAchievementInfoEvent>
{
    private readonly AchievementsService achievementsService;

    public FullAchievementInfoEventHandler(AchievementsService achievementsService) : base((int)EventCodes.FullAchievementInfo)
    {
        this.achievementsService = achievementsService;
    }

    protected override async Task OnActionAsync(FullAchievementInfoEvent value)
    {
        var indices = value.AchievementsIndex;
        var levels = value.AchievementLevels;
        var level100Indices = value.AchievementsIndexLevel100;
        if (indices is null || levels is null)
        {
            indices = Array.Empty<short>();
            levels = Array.Empty<byte>();
        }

        var levelByIndex = new Dictionary<int, byte>();
        int count = Math.Min(indices.Length, levels.Length);
        for (int i = 0; i < count; i++)
        {
            levelByIndex[indices[i]] = levels[i];
        }

        if (level100Indices != null)
        {
            for (int i = 0; i < level100Indices.Length; i++)
            {
                var index = level100Indices[i];
                var info = achievementsService.GetAchievementInfoByIndex(index);
                levelByIndex[index] = info.IsTemplate ? (byte)100 : (byte)1;
            }
        }

        if (levelByIndex.Count == 0)
        {
            await Task.CompletedTask;
            return;
        }

        var keys = new List<int>(levelByIndex.Keys);
        keys.Sort();

        var achievements = new List<AchievementInfo>(keys.Count);
        foreach (var index in keys)
        {
            var info = achievementsService.GetAchievementInfoByIndex(index);
            achievements.Add(new AchievementInfo(info.Id, levelByIndex[index]));
        }

        achievements.ForEach(a => Log.Information("Achievement {AchievementId} is at level {Level}.", a.Id, a.Level));

        await Task.CompletedTask;
    }
}
