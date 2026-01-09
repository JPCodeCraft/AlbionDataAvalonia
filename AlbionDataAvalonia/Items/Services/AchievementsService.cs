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
        public readonly record struct AchievementDefinition(int Index, string Id);

        private const string XmlUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/achievements.xml";
        private readonly Dictionary<int, string> achievementMappings = new();
        private readonly List<AchievementDefinition> achievements = new();

        public IReadOnlyList<AchievementDefinition> Achievements => achievements;

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
                            if (!string.Equals(name, "achievement", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(name, "templateachievement", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var id = element.Attribute("id")?.Value;
                            if (string.IsNullOrWhiteSpace(id))
                            {
                                continue;
                            }

                            achievements.Add(new AchievementDefinition(index, id));
                            achievementMappings[index] = id;
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

        public string GetAchievementIdByIndex(int index)
        {
            if (achievementMappings.TryGetValue(index, out var id))
            {
                return id;
            }

            return $"Unknown Achievement ({index})";
        }
    }
}
