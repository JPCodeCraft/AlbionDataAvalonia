using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia
{
    public static class ClientUpdater
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task CheckForUpdatesAsync(string currentVersion, string versionUrl)
        {
            try
            {
                var response = await httpClient.GetStringAsync(versionUrl);
                var jsonDocument = JsonDocument.Parse(response);
                var latestVersion = jsonDocument.RootElement.GetProperty("version").GetString();

                if (string.Compare(currentVersion, latestVersion) < 0)
                {
                    Console.WriteLine("A new version is available.");
                }
                else
                {
                    Console.WriteLine("You are using the latest version.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while checking for updates: {ex.Message}");
            }
        }
    }
}
