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
        private static List<LocationJson> locations = new List<LocationJson>();
        private static List<AlbionLocation> albionLocations = new List<AlbionLocation>();

        private static readonly Dictionary<string, string> HellDenToMarket = new()
    {
        { "0000-HellDen", "0007" },
        { "1000-HellDen", "1002" },
        { "2000-HellDen", "2004" },
        { "3004-HellDen", "3008" },
        { "3003-HellDen", "3005" },
        { "4000-HellDen", "4002" },
        { "5000-HellDen", "5003" }
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
                        location.MarketLocation = marketId.HasValue ? AlbionLocations.GetByIntId(marketId.Value) : null;
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


        public static List<AlbionLocation> GetAll() => albionLocations;

        public static AlbionLocation? Get(string query)
        {
            var found = albionLocations
                .SingleOrDefault(location =>
                    location.Id.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || location.Name.Equals(query, StringComparison.OrdinalIgnoreCase)
                    || location.FriendlyName.Replace(" ", "").Equals(
                        query.Replace(" ", "").Replace("@", "").Replace("_", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(location.Id, out var locIdInt) && int.TryParse(query, out var nameInt) && locIdInt == nameInt)
                );
            return found;
        }

        public static AlbionLocation GetByIntId(int id)
        {
            var found = albionLocations.SingleOrDefault(location => location.IdInt == id);
            return found ?? Unknown;
        }

        // Id of the actual market that's used in the location
        public static int? GetMarketLocationIdInt(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
            {
                return null;
            }

            var id = locationId;

            if (id.EndsWith("-Auction2"))
            {
                id = id.Replace("-Auction2", "");
            }
            else if (id.EndsWith("-HellDen"))
            {
                if (HellDenToMarket.TryGetValue(id, out var marketId))
                {
                    id = marketId;
                }
                else
                {
                    return null; // Not found
                }
            }
            else if (id.Contains('@'))
            {
                id = id.Split('@', 2).Last();
                if (id.Contains("BLACKBANK-"))
                {
                    id = id.Replace("BLACKBANK-", "");
                }
            }
            else if (id.StartsWith("BLACKBANK-"))
            {
                id = id.Replace("BLACKBANK-", "");
            }

            if (int.TryParse(id, out var intId))
            {
                if (PortalToCity.TryGetValue(intId, out var cityId))
                {
                    return cityId;
                }
                return intId;
            }

            return null; // Parsing failed
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