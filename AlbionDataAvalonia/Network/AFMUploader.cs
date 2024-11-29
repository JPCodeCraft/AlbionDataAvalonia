using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class AFMUploader : IDisposable
    {
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;

        private readonly HttpClient httpClient = new HttpClient();

        public AFMUploader(PlayerState playerState, SettingsManager settingsManager)
        {
            _playerState = playerState;
            _settingsManager = settingsManager;

            string afmBaseUrl = "https://api.albionfreemarket.com";

            httpClient.BaseAddress = new Uri(afmBaseUrl);
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://github.com/JPCodeCraft/AlbionDataAvalonia");
        }

        public void UploadPlayerCount(PlayerCount playerCount)
        {
            _ = Upload(playerCount);
        }

        private async Task Upload(PlayerCount playerCount)
        {
            var requestUri = new Uri(httpClient.BaseAddress, "/dataclient/playercount/");
            HttpResponseMessage response = await httpClient.PostAsJsonAsync(requestUri, playerCount);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Log.Error("HTTP Error while uploading player count. Returned: {0} ({1}).", response.StatusCode, await response.Content.ReadAsStringAsync());
                return;
            }

            Log.Debug("Successfully sent player count to {0}.", requestUri);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}