using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public class ItemsIdsService
    {
        private class ItemIdEntry
        {
            public string UniqueName { get; set; } = string.Empty;
            public string UsName { get; set; } = string.Empty;
        }

        private class ItemJsonEntry
        {
            public string Index { get; set; } = string.Empty;
            public string UniqueName { get; set; } = string.Empty;
            public Dictionary<string, string>? LocalizedNames { get; set; }
        }

        private const string JsonUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/formatted/items.json";
        private Dictionary<int, ItemIdEntry> itemMappings = new();
        private Dictionary<string, string> itemNamesByUniqueName = new(StringComparer.OrdinalIgnoreCase);

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing ItemsIds service...");
                using (var httpClient = new HttpClient())
                {
                    var json = await httpClient.GetStringAsync(JsonUrl);
                    var items = JsonSerializer.Deserialize<List<ItemJsonEntry>>(json);
                    if (items is not null)
                    {
                        foreach (var item in items)
                        {
                            if (int.TryParse(item.Index, out int id)
                                && !string.IsNullOrWhiteSpace(item.UniqueName))
                            {
                                var usName = item.LocalizedNames is not null
                                    && item.LocalizedNames.TryGetValue("EN-US", out var localizedUsName)
                                    ? localizedUsName
                                    : string.Empty;
                                var resolvedUsName = string.IsNullOrWhiteSpace(usName)
                                    ? item.UniqueName
                                    : ItemNameFormatter.FormatUsName(item.UniqueName, usName);
                                itemMappings[id] = new ItemIdEntry
                                {
                                    UniqueName = item.UniqueName,
                                    UsName = resolvedUsName
                                };
                                itemNamesByUniqueName[item.UniqueName] = resolvedUsName;
                            }
                        }
                    }
                }
                Log.Information("ItemsIds service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize ItemsIds service.");
            }
        }

        public (string UniqueName, string UsName) GetItemById(int itemId)
        {
            if (itemMappings.TryGetValue(itemId, out var itemEntry))
            {
                return (itemEntry.UniqueName, itemEntry.UsName);
            }

            return ("Unknown Item", $"Unknown Item ({itemId})");
        }

        public string GetUsNameByUniqueName(string uniqueName)
        {
            if (itemNamesByUniqueName.TryGetValue(uniqueName, out var usName))
            {
                return usName;
            }

            return uniqueName;
        }
    }
}
