using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public class AchievementsService
    {
        private sealed class AchievementsRoot
        {
            [JsonPropertyName("achievements")]
            public AchievementsContainer? Achievements { get; set; }
        }

        private sealed class AchievementsContainer
        {
            [JsonPropertyName("achievement")]
            public AchievementEntry[]? Achievement { get; set; }

            [JsonPropertyName("templateachievement")]
            public AchievementEntry[]? TemplateAchievement { get; set; }
        }

        private sealed class AchievementEntry
        {
            [JsonPropertyName("@id")]
            public string? Id { get; set; }
        }

        public readonly record struct AchievementDefinition(int Index, string Id);

        private const string JsonUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/achievements.json";
        private readonly Dictionary<int, string> achievementMappings = new();
        private readonly Dictionary<int, string> templateAchievementMappings = new();
        private readonly List<AchievementDefinition> achievements = new();
        private readonly List<AchievementDefinition> templateAchievements = new();

        public IReadOnlyList<AchievementDefinition> Achievements => achievements;
        public IReadOnlyList<AchievementDefinition> TemplateAchievements => templateAchievements;

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing Achievements service...");
                using (var httpClient = new HttpClient())
                {
                    var json = await httpClient.GetStringAsync(JsonUrl);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var root = JsonSerializer.Deserialize<AchievementsRoot>(json);
                        var achievementsRoot = root?.Achievements;

                        achievementMappings.Clear();
                        templateAchievementMappings.Clear();
                        achievements.Clear();
                        templateAchievements.Clear();

                        if (achievementsRoot?.Achievement != null)
                        {
                            for (int i = 0; i < achievementsRoot.Achievement.Length; i++)
                            {
                                var id = achievementsRoot.Achievement[i].Id;
                                if (string.IsNullOrWhiteSpace(id))
                                {
                                    continue;
                                }

                                achievements.Add(new AchievementDefinition(i, id));
                                achievementMappings[i] = id;
                            }
                        }

                        if (achievementsRoot?.TemplateAchievement != null)
                        {
                            for (int i = 0; i < achievementsRoot.TemplateAchievement.Length; i++)
                            {
                                var id = achievementsRoot.TemplateAchievement[i].Id;
                                if (string.IsNullOrWhiteSpace(id))
                                {
                                    continue;
                                }

                                templateAchievements.Add(new AchievementDefinition(i, id));
                                templateAchievementMappings[i] = id;
                            }
                        }
                    }
                }
                Log.Information("Achievements service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize Achievements service.");
            }
        }

        public string GetTemplateAchievementIdByIndex(int index)
        {
            if (templateAchievementMappings.TryGetValue(index, out var templateId))
            {
                return templateId;
            }

            return $"Unknown Template Achievement ({index})";
        }
    }
}
