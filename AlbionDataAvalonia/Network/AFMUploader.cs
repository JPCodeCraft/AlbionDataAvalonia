﻿using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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

        private readonly object _headersLock = new();

        public AFMUploader(PlayerState playerState, SettingsManager settingsManager, AuthService authService)
        {
            _playerState = playerState;
            _settingsManager = settingsManager;
            _authService = authService;

            _authService.FirebaseUserChanged += (user) => UpdateAuthHeader(user);
        }

        private void UpdateAuthHeader(FirebaseAuthResponse? user)
        {
            lock (_headersLock)
            {
                try
                {
                    if (user is not null &&
                        !string.IsNullOrWhiteSpace(user.IdToken) &&
                        !string.IsNullOrWhiteSpace(user.LocalId))
                    {
                        var newAuth = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.IdToken);

                        // Only set if changed to reduce churn
                        if (!Equals(httpClient.DefaultRequestHeaders.Authorization, newAuth))
                        {
                            httpClient.DefaultRequestHeaders.Authorization = newAuth;
                        }

                        // Replace X-User-Id safely, avoiding validation exceptions
                        httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                        if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-User-Id", user.LocalId))
                        {
                            Log.Warning("Failed to set X-User-Id header due to validation.");
                        }

                        Log.Debug("Set AFM upload auth header for user");
                    }
                    else
                    {
                        httpClient.DefaultRequestHeaders.Authorization = null;
                        httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                        Log.Debug("Cleared AFM upload auth header, since no user is logged in");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while updating AFM upload auth header; clearing headers as a fallback.");
                    httpClient.DefaultRequestHeaders.Authorization = null;
                    httpClient.DefaultRequestHeaders.Remove("X-User-Id");
                }
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

            var hasValidToken = await _authService.EnsureValidTokenAsync();
            if (!hasValidToken)
            {
                Log.Error("Cannot upload market order without a valid Firebase session.");
                return UploadStatus.Failed;
            }

            var firebaseUserId = _authService.FirebaseUserId;
            if (firebaseUserId is null)
            {
                Log.Error("Cannot upload market order without a Firebase user ID.");
                return UploadStatus.Failed;
            }

            var afmMarketUpload = new AfmMarketUpload(marketUpload, _playerState.AlbionServer.Id, firebaseUserId);

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            JsonNode? jsonNode = JsonSerializer.SerializeToNode(afmMarketUpload, serializerOptions);

            if (jsonNode is JsonObject jsonObject && jsonObject["orders"] is JsonArray ordersArray)
            {
                for (int i = 0; i < ordersArray.Count; i++)
                {
                    var originalOrder = afmMarketUpload.Orders[i];
                    var orderNode = ordersArray[i]?.AsObject();
                    if (orderNode != null)
                    {
                        orderNode["locationId"] = originalOrder.Location.MarketLocation?.IdInt?.ToString() ?? "0";
                    }
                }
            }

            var requestUri = new Uri(httpClient.BaseAddress, "flipperOrders?contributeToPublic=" + _playerState.ContributeToPublic);
            var payload = jsonNode?.ToJsonString(serializerOptions) ?? "{}";

            async Task<HttpResponseMessage> SendAsync()
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                return await httpClient.PostAsync(requestUri, content);
            }

            HttpResponseMessage? response = null;

            try
            {
                response = await SendAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    response = null;

                    var recovered = await _authService.TryRecoverFromUnauthorizedAsync();
                    if (!recovered)
                    {
                        return UploadStatus.Failed;
                    }

                    response = await SendAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        var unauthorizedBody = await response.Content.ReadAsStringAsync();
                        Log.Error("AFM upload unauthorized after retry. Returned: {0} ({1}).", response.StatusCode, unauthorizedBody);
                        return UploadStatus.Failed;
                    }
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("HTTP Error while uploading AfmMarketUpload to Url {0}. Returned: {1} ({2}).", requestUri.ToString(), response.StatusCode, errorContent);
                    return UploadStatus.Failed;
                }

                Log.Debug("Successfully sent AfmMarketUpload to {0}.", requestUri);
                return UploadStatus.Success;
            }
            finally
            {
                response?.Dispose();
            }
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
