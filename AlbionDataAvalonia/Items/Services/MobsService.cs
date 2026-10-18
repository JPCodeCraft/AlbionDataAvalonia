using AlbionDataAvalonia.ReferenceData;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Items.Services
{
    public sealed record MobDescriptor(int Index, string? UniqueName, string DisplayName);

    public class MobsService
    {
        private const string JsonUrl = "https://cdn.albionfreemarket.com/AlbionLocalization/processed_mobs.json";

        private Dictionary<int, MobEntry> mobsById = new();
        private Dictionary<string, string> uniqueNamesByName = new(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded => mobsById.Count > 0;

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing mobs service...");
                var loaded = await ReferenceDataLoader.Shared.LoadAsync(JsonUrl, ParseMobs);
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mob in loaded.Values.OrderBy(x => x.MobId))
                    if (!string.IsNullOrWhiteSpace(mob.En) && !string.IsNullOrWhiteSpace(mob.UniqueName)) names.TryAdd(mob.En, mob.UniqueName);
                uniqueNamesByName = names;
                mobsById = loaded;

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

        /// <summary>
        /// The unique name of the first mob with this English name. Events that saved only a mob's name (damage, not
        /// kills) find its portrait this way; the tiers of one mob share a name and a portrait.
        /// </summary>
        public string? FindUniqueName(string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : uniqueNamesByName.GetValueOrDefault(name.Trim());

        public MobDescriptor? GetMobDescriptor(int? index)
        {
            if (index is not { } value || !mobsById.TryGetValue(value, out var mob)) return null;
            return new MobDescriptor(value, mob.UniqueName, GetMobName(value) ?? $"Mob {value}");
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
