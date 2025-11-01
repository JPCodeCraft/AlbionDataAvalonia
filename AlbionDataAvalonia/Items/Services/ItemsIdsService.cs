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
            public string UniqueName { get; set; }
            public string UsName { get; set; }
        }

        private const string TxtUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/formatted/items.txt";
        private Dictionary<int, ItemIdEntry> itemMappings = new();

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

                            // Split by " : " to separate the parts
                            var parts = trimmedLine.Split(new[] { " : " }, 2, StringSplitOptions.None);
                            if (parts.Length == 2)
                            {
                                var idAndUnique = parts[0].Trim();
                                var usName = parts[1].Trim();

                                // Find the colon in the first part to separate id and unique name
                                var colonIndex = idAndUnique.IndexOf(':');
                                if (colonIndex > 0)
                                {
                                    var idStr = idAndUnique.Substring(0, colonIndex).Trim();
                                    var uniqueName = idAndUnique.Substring(colonIndex + 1).Trim();

                                    if (int.TryParse(idStr, out int id))
                                    {
                                        itemMappings[id] = new ItemIdEntry { UniqueName = uniqueName, UsName = usName };
                                    }
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
    }
}