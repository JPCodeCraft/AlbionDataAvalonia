using Serilog;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services;

public class MobsService
{
    private const string JsonUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/processed_mobs.json";

    private Dictionary<int, MobEntry> mobsById = new();

    public async Task InitializeAsync()
    {
        try
        {
            Log.Information("Initializing mobs service...");
            using (var httpClient = new HttpClient())
            {
                var json = await httpClient.GetStringAsync(JsonUrl);
                if (!string.IsNullOrEmpty(json))
                {
                    var mobs = JsonSerializer.Deserialize<List<MobEntry>>(json) ?? new List<MobEntry>();
                    var loadedMobs = new Dictionary<int, MobEntry>();
                    foreach (var mob in mobs)
                    {
                        if (mob.MobId > 0)
                        {
                            loadedMobs[mob.MobId] = mob;
                        }
                    }

                    mobsById = loadedMobs;
                }
            }

            Log.Information("Mobs service initialized with {MobCount} mob mappings.", mobsById.Count);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to initialize mobs service.");
        }
    }

    public string? GetMobName(int? mobId)
    {
        if (mobId is not { } value || !mobsById.TryGetValue(value, out var mob))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(mob.En))
        {
            return mob.En;
        }

        return string.IsNullOrWhiteSpace(mob.UniqueName)
            ? null
            : mob.UniqueName;
    }

    private sealed class MobEntry
    {
        [JsonPropertyName("mobId")]
        public int MobId { get; set; }

        [JsonPropertyName("uniqueName")]
        public string? UniqueName { get; set; }

        [JsonPropertyName("en")]
        public string? En { get; set; }
    }
}
