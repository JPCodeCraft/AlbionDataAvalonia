using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public class ItemsIdsService
    {
        private class ItemIdEntry
        {
            public string UniqueName { get; set; }
            public string UsName { get; set; }
        }

        private const string TxtUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/formatted/items.txt";
        private Dictionary<int, ItemIdEntry> itemMappings = new();
        private Dictionary<string, string> itemNamesByUniqueName = new(StringComparer.OrdinalIgnoreCase);

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing ItemsIds service...");
                using (var httpClient = new HttpClient())
                {
                    var txt = await httpClient.GetStringAsync(TxtUrl);
                    if (!string.IsNullOrEmpty(txt))
                    {
                        var lines = txt.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmedLine)) continue;

                            var parts = trimmedLine.Split(new[] { " : " }, 2, StringSplitOptions.None);
                            var idAndUnique = parts[0].Trim();
                            var usName = parts.Length == 2
                                ? parts[1].Trim()
                                : string.Empty;

                            var colonIndex = idAndUnique.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                var idStr = idAndUnique.Substring(0, colonIndex).Trim();
                                var uniqueName = idAndUnique.Substring(colonIndex + 1).Trim();

                                if (int.TryParse(idStr, out int id) && !string.IsNullOrWhiteSpace(uniqueName))
                                {
                                    var resolvedUsName = string.IsNullOrWhiteSpace(usName)
                                        ? uniqueName
                                        : ItemNameFormatter.FormatUsName(uniqueName, usName);
                                    itemMappings[id] = new ItemIdEntry
                                    {
                                        UniqueName = uniqueName,
                                        UsName = resolvedUsName
                                    };
                                    itemNamesByUniqueName[uniqueName] = resolvedUsName;
                                }
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
