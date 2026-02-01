using Albion.Network;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Serilog;

namespace AlbionDataAvalonia.Network.Handlers;

public class FullAchievementInfoEventHandler : EventPacketHandler<FullAchievementInfoEvent>
{
    private readonly AchievementsService achievementsService;
    private readonly PlayerState playerState;
    private readonly AFMUploader afmUploader;
    private readonly SettingsManager settingsManager;

    public FullAchievementInfoEventHandler(AchievementsService achievementsService, PlayerState playerState, AFMUploader afmUploader, SettingsManager settingsManager) : base((int)EventCodes.FullAchievementInfo)
    {
        this.achievementsService = achievementsService;
        this.playerState = playerState;
        this.afmUploader = afmUploader;
        this.settingsManager = settingsManager;
    }

    protected override async Task OnActionAsync(FullAchievementInfoEvent value)
    {
        if (!settingsManager.UserSettings.UploadSpecsToAfm)
        {
            await Task.CompletedTask;
            Log.Debug("Not uploading achievements, upload to AFM is disabled in settings.");
            return;
        }

        if (playerState.AlbionServer is null || string.IsNullOrWhiteSpace(playerState.PlayerName))
        {
            await Task.CompletedTask;
            Log.Warning("Not uploading achievements, player state is not ready.");
            return;
        }

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
            Log.Warning("Not uploading achievements, no achievements to upload.");
            return;
        }

        var keys = new List<int>(levelByIndex.Keys);
        keys.Sort();

        var achievements = new List<AchievementUploadEntry>(keys.Count);
        foreach (var index in keys)
        {
            var info = achievementsService.GetAchievementInfoByIndex(index);
            achievements.Add(new AchievementUploadEntry
            {
                Id = info.Id,
                Level = levelByIndex[index]
            });
        }

        var upload = new AchievementUpload
        {
            CharacterName = playerState.PlayerName,
            ServerId = playerState.AlbionServer.Id,
            Achievements = achievements
        };

        afmUploader.UploadAchievements(upload);

        await Task.CompletedTask;
    }
}
