﻿using AlbionDataAvalonia.Locations.Models;
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
        private const string JsonUrl = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/world.json";
        private static List<LocationJson> locations = new List<LocationJson>();
        private static List<AlbionLocation> albionLocations = new List<AlbionLocation>();

        public static AlbionLocation Unknown { get; } = new AlbionLocation(0, "Unknown", "Unknown");
        public static AlbionLocation Unset { get; } = new AlbionLocation(-1, "Unset", "Unset");

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
                    if (int.TryParse(location.Index, out var id))
                    {
                        albionLocations.Add(new AlbionLocation(id, location.UniqueName.Replace(" ", ""), location.UniqueName));
                    }
                }

                // add unknown location
                albionLocations.Add(Unknown);

                // add unset location
                albionLocations.Add(Unset);

                Log.Information("Locations service initialized.");
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to initialize localization service.");
            }
        }

        public static List<AlbionLocation> GetAll() => albionLocations;

        public static AlbionLocation? Get(string name) => albionLocations
            .SingleOrDefault(location => location.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || location.FriendlyName.Replace(" ", "").Equals(name.Replace(" ", "").Replace("@", "").Replace("_", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));

        public static AlbionLocation? Get(int id) => albionLocations.SingleOrDefault(location => location.Id == id);


        public static bool TryParse(string info, out AlbionLocation? location)
        {
            // try to parse to an int, then its Id
            if (int.TryParse(info, out var id))
            {
                location = Get(id);
            }
            else
            {
                location = Get(info);
            }

            return location != null;
        }
    }
}