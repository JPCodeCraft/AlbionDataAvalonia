using AlbionDataAvalonia.Locations.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Locations
{
    public static class AlbionLocations
    {
        private const string JsonUrl = "https://cdn.albionfreemarket.com/ao-bin-dumps/formatted/world.json";
        private static List<AlbionLocation> albionLocations = new List<AlbionLocation>();

        private static readonly Dictionary<string, int> HellDenToMarket = new()
    {
        { "0000-HellDen", 7 },
        { "1000-HellDen", 1002 },
        { "2000-HellDen", 2004 },
        { "3004-HellDen", 3008 },
        { "3003-HellDen", 3005 },
        { "4000-HellDen", 4002 },
        { "5000-HellDen", 5003 }
    };

        private static readonly Dictionary<int, int> PortalToCity = new()
    {
        { 301, 7 },
        { 1301, 1002 },
        { 2301, 2004 },
        { 3301, 3008 },
        { 4301, 4002 },
        { 3013, 3005 }
    };

        public static AlbionLocation Unknown { get; } = new AlbionLocation("-0002", "Unknown", "Unknown");
        public static AlbionLocation Unset { get; } = new AlbionLocation("-0001", "Unset", "Unset");

        public static async Task InitializeAsync()
        {
            try
            {
                Log.Information("Initializing locations service...");
                var locations = new List<LocationJson>();
                using (var httpClient = new HttpClient())
                {
                    var json = await httpClient.GetStringAsync(JsonUrl);
                    if (!string.IsNullOrEmpty(json))
                    {
                        locations = JsonSerializer.Deserialize<LocationJson[]>(json)?.ToList() ?? new();
                    }
                }

                foreach (var location in locations)
                {
                    if (location.UniqueName == "Caerleon") location.UniqueName = "Black Market";
                    albionLocations.Add(new AlbionLocation(location.Index, location.UniqueName.Replace(" ", ""), location.UniqueName));
                }

                // add unknown location
                albionLocations.Add(Unknown);

                // add unset location
                albionLocations.Add(Unset);

                // set locations markets and friendly names
                foreach (var location in albionLocations)
                {
                    var marketId = GetMarketLocationIdInt(location.Id);
                    if (marketId.HasValue)
                    {
                        location.MarketLocation = GetByIntId(marketId.Value);
                    }
                    if (location.MarketLocation != null && location.Id != location.MarketLocation.Id)
                    {
                        location.FriendlyName = $"{location.FriendlyName} ({location.MarketLocation.FriendlyName})";
                    }
                }

                Log.Information("Locations service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize localization service.");
            }
        }


        public static AlbionLocation? Get(string query)
        {
            static string Normalize(string value) =>
                value.Replace(" ", "").Replace("@", "").Replace("_", "").Replace("-", "");

            var normalizedQuery = Normalize(query);

            var found = albionLocations
                .SingleOrDefault(location =>
                    location.Id.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || location.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || Normalize(location.FriendlyName).Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(location.Id, out var locIdInt) && int.TryParse(query, out var nameInt) && locIdInt == nameInt)
                );
            return found;
        }

        public static AlbionLocation ResolveLocation(string rawLocationId)
        {
            if (string.IsNullOrWhiteSpace(rawLocationId))
            {
                return Unknown;
            }

            foreach (var candidate in GetLocationCandidates(rawLocationId))
            {
                var location = Get(candidate);
                if (location != null)
                {
                    return location;
                }
            }

            var marketLocationId = GetMarketLocationIdInt(rawLocationId);
            if (marketLocationId.HasValue)
            {
                return GetByIntId(marketLocationId.Value);
            }

            return Unknown;
        }

        public static int ResolveMarketLocationId(string rawLocationId)
        {
            var location = ResolveLocation(rawLocationId);
            if (location != Unknown)
            {
                return location.MarketLocation?.IdInt ?? location.IdInt ?? Unknown.IdInt ?? -2;
            }

            return GetMarketLocationIdInt(rawLocationId) ?? Unknown.IdInt ?? -2;
        }

        public static AlbionLocation ResolveStoredLocation(string rawLocationId, int locationId)
        {
            if (!string.IsNullOrWhiteSpace(rawLocationId))
            {
                var location = ResolveLocation(rawLocationId);
                if (location != Unknown)
                {
                    return location;
                }
            }

            return GetByIntId(locationId);
        }

        private static AlbionLocation GetByIntId(int id)
        {
            var found = albionLocations.SingleOrDefault(location => location.IdInt == id);
            return found ?? Unknown;
        }

        // Id of the actual market that's used in the location
        private static int? GetMarketLocationIdInt(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
            {
                return null;
            }

            foreach (var candidate in GetLocationCandidates(locationId))
            {
                var marketId = GetMarketLocationIdIntFromCandidate(candidate);
                if (marketId.HasValue)
                {
                    return marketId.Value;
                }
            }

            return null; // Parsing failed
        }

        private static IEnumerable<string> GetLocationCandidates(string rawLocationId)
        {
            var trimmed = rawLocationId.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                yield break;
            }

            yield return trimmed;

            var parts = trimmed
                .Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Reverse();

            foreach (var part in parts)
            {
                if (!string.Equals(part, trimmed, StringComparison.Ordinal))
                {
                    yield return part;
                }
            }
        }

        private static int? GetMarketLocationIdIntFromCandidate(string candidate)
        {
            var id = candidate.Trim();
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            id = id.TrimStart('@');

            if (id.Equals("BLACK_MARKET", StringComparison.OrdinalIgnoreCase)
                || id.Equals("BLACKMARKET", StringComparison.OrdinalIgnoreCase))
            {
                return 3003;
            }

            var changed = true;
            while (changed)
            {
                changed = false;

                if (id.EndsWith("-Auction2", StringComparison.OrdinalIgnoreCase))
                {
                    id = id[..^"-Auction2".Length];
                    changed = true;
                }

                if (id.StartsWith("BLACKBANK-", StringComparison.OrdinalIgnoreCase))
                {
                    id = id["BLACKBANK-".Length..];
                    changed = true;
                }
            }

            if (id.EndsWith("-HellDen", StringComparison.OrdinalIgnoreCase))
            {
                return HellDenToMarket.TryGetValue(id, out var marketId) ? marketId : null;
            }

            if (int.TryParse(id, out var intId))
            {
                return PortalToCity.TryGetValue(intId, out var cityId) ? cityId : intId;
            }

            return null;
        }

        // Only for locations that can be directly parsed to an int. All markets are.
        public static int? GetLocationIdInt(string locationId)
        {
            if (int.TryParse(locationId, out var intId))
            {
                return intId;
            }

            return null; // Not a valid int location ID
        }
    }
}
