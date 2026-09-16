using AlbionDataAvalonia.ReferenceData;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public class MobsService
    {
        private const string JsonUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/processed_mobs.json";

        private Dictionary<int, MobEntry> mobsById = new();

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing mobs service...");
                mobsById = await ReferenceDataLoader.Shared.LoadAsync(JsonUrl, ParseMobs);

                Log.Information("Mobs service initialized with {MobCount} mob mappings.", mobsById.Count);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize mobs service.");
            }
        }

        private static Dictionary<int, MobEntry> ParseMobs(string json)
        {
            var mobs = JsonSerializer.Deserialize<List<MobEntry>>(json)
                ?? throw new InvalidDataException("Mob data is null.");
            var loadedMobs = new Dictionary<int, MobEntry>();
            foreach (var mob in mobs)
            {
                if (mob.MobId > 0 && (!string.IsNullOrWhiteSpace(mob.En) || !string.IsNullOrWhiteSpace(mob.UniqueName)))
                {
                    loadedMobs.Add(mob.MobId, mob);
                }
            }
            if (loadedMobs.Count == 0)
            {
                throw new InvalidDataException("Mob data contains no named mob mappings.");
            }
            return loadedMobs;
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
}
