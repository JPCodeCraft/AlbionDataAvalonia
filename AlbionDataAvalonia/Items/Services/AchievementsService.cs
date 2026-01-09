using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AlbionDataAvalonia.Items.Services
{
    public class AchievementsService
    {
        public readonly record struct AchievementInfo(string Id, bool IsTemplate);

        private const string XmlUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/achievements.xml";
        private readonly Dictionary<int, AchievementInfo> achievementMappings = new();
        private readonly List<AchievementInfo> achievements = new();

        public IReadOnlyList<AchievementInfo> Achievements => achievements;

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing Achievements service...");
                using (var httpClient = new HttpClient())
                {
                    var xml = await httpClient.GetStringAsync(XmlUrl);
                    if (!string.IsNullOrEmpty(xml))
                    {
                        achievementMappings.Clear();
                        achievements.Clear();

                        var document = XDocument.Parse(xml);
                        var achievementsElement = document.Root;
                        if (achievementsElement == null)
                        {
                            Log.Warning("Achievements XML is missing root element.");
                            return;
                        }

                        int index = 0;
                        foreach (var element in achievementsElement.Elements())
                        {
                            var name = element.Name.LocalName;
                            var isAchievement = string.Equals(name, "achievement", StringComparison.OrdinalIgnoreCase);
                            var isTemplateAchievement = string.Equals(name, "templateachievement", StringComparison.OrdinalIgnoreCase);
                            if (!isAchievement && !isTemplateAchievement)
                            {
                                continue;
                            }

                            var id = element.Attribute("id")?.Value;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            var isTemplate = isTemplateAchievement;
                            achievements.Add(new AchievementInfo(id, isTemplate));
                            achievementMappings[index] = new AchievementInfo(id, isTemplate);
                            index++;
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

        public AchievementInfo GetAchievementInfoByIndex(int index)
        {
            if (achievementMappings.TryGetValue(index, out var info))
            {
                return info;
            }

            return new AchievementInfo($"Unknown Achievement ({index})", false);
        }
    }
}
