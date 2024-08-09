using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Localization.Services
{
    public class LocalizationService
    {
        private const string JsonUrl = "https://raw.githubusercontent.com/JPCodeCraft/AlbionFormattedItemsParser/main/us_name_mappings.json";
        private Dictionary<string, string> nameMappings = new();

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing localization service...");
                using (var httpClient = new HttpClient())
                {
                    var json = await httpClient.GetStringAsync(JsonUrl);
                    if (!string.IsNullOrEmpty(json))
                    {
                        nameMappings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                    }
                }
                Log.Information("Localization service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize localization service.");
            }
        }

        public string GetUsName(string uniqueName)
        {
            if (nameMappings.TryGetValue(uniqueName, out var usName))
            {
                var parts = uniqueName.Split('_');
                var tier = parts[0].Substring(1); // Get the number after 'T'

                var enchantment = 0;
                if (uniqueName.Contains("@"))
                {
                    enchantment = int.Parse(uniqueName.Split('@')[1]); // Get the number after '@'
                }

                return $"{usName} [{tier}.{enchantment}]";
            }

            return uniqueName;
        }
    }
}