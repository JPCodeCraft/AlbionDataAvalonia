using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class AFMUploader : IDisposable
    {
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;
        private readonly AuthService _authService;

        private readonly HttpClient httpClient = new HttpClient();

        public AFMUploader(PlayerState playerState, SettingsManager settingsManager, AuthService authService)
        {
            _playerState = playerState;
            _settingsManager = settingsManager;
            _authService = authService;

            _authService.FirebaseUserChanged += (user) => updateAuthHeader(user);
        }

        private void updateAuthHeader(FirebaseAuthResponse? user)
        {
            if (user is not null)
            {
                httpClient.DefaultRequestHeaders.Authorization = new("Bearer", user.IdToken);
                httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                httpClient.DefaultRequestHeaders.Add("X-User-Id", user.LocalId);

            }
            else
            {
                httpClient.DefaultRequestHeaders.Authorization = null;
                httpClient.DefaultRequestHeaders.Remove("X-User-Id");
            }
        }

        public void Initialize()
        {
            httpClient.BaseAddress = new Uri(_settingsManager.AppSettings.AfmTopItemsApiBase);
            httpClient.DefaultRequestHeaders.Referrer = new Uri("https://github.com/JPCodeCraft/AlbionDataAvalonia");
        }

        public async Task<UploadStatus> UploadMarketOrder(MarketUpload marketUpload)
        {
            if (_playerState.AlbionServer is null)
            {
                Log.Error("Cannot upload market order without a server.");
                return UploadStatus.Failed;
            }
            if (_authService.FirebaseUserId is null)
            {
                Log.Error("Cannot upload market order without a Firebase user ID.");
                return UploadStatus.Failed;
            }

            var afmMarketUpload = new AfmMarketUpload(marketUpload, _playerState.AlbionServer.Id, _authService.FirebaseUserId);

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Serialize to a JsonNode to manipulate it
            JsonNode? jsonNode = JsonSerializer.SerializeToNode(afmMarketUpload, serializerOptions);

            if (jsonNode is JsonObject jsonObject && jsonObject["orders"] is JsonArray ordersArray)
            {
                for (int i = 0; i < ordersArray.Count; i++)
                {
                    var originalOrder = afmMarketUpload.Orders[i];
                    var orderNode = ordersArray[i]?.AsObject();
                    if (orderNode != null)
                    {
                        // Replace string LocationId with the integer AODPLocationIdInt
                        orderNode["locationId"] = originalOrder.Location.MarketLocation?.IdInt?.ToString() ?? "0";
                    }
                }
            }

            var requestUri = new Uri(httpClient.BaseAddress, "flipperOrders?contributeToPublic=" + _playerState.ContributeToPublic);
            var jsonContent = JsonContent.Create(jsonNode, options: serializerOptions);

            HttpResponseMessage response = await httpClient.PostAsync(requestUri, jsonContent);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Log.Error("HTTP Error while uploading AfmMarketUpload to Url {0}. Returned: {1} ({2}).", requestUri.ToString(), response.StatusCode, await response.Content.ReadAsStringAsync());
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await _authService.ForceTokenRefreshAsync();
                }
                return UploadStatus.Failed;
            }

            Log.Debug("Successfully sent AfmMarketUpload to {0}.", requestUri);
            return UploadStatus.Success;
        }

        public void UploadPlayerCount(PlayerCount playerCount)
        {
            _ = Upload(playerCount);
        }

        private async Task Upload(PlayerCount playerCount)
        {
            var requestUri = new Uri(httpClient.BaseAddress, "playercount");
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