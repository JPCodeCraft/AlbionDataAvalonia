using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Auth.Services
{
    public class AuthService
    {
        private readonly SettingsManager _settingsManager;
        public FirebaseAuthResponse? FirebaseUser { get; private set; } = null;

        public AuthService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        public async Task SignInAsync()
        {
            Log.Debug("Starting sign in...");
            try
            {
                // Start listening for the redirect in a separate task
                var codeTask = HandleRedirectAndGetAuthCodeAsync();

                // Initiate the sign-in process
                SignInWithGoogle();

                // Await the token retrieval
                var code = await codeTask;

                FirebaseUser = await GetFirebaseUserAsync(code);
            }
            catch (Exception ex)
            {
                Log.Error($"Sign-in failed: {ex.Message}");
                throw;
            }
        }

        public void SignInWithGoogle()
        {
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                          $"?client_id={_settingsManager.AppSettings.AfmAuthClientId}" +
                          $"&redirect_uri={Uri.EscapeDataString(_settingsManager.AppSettings.AfmAuthRedirectUri)}" +
                          $"&response_type=code" +
                          $"&scope=openid%20email%20profile" +
                          $"&access_type=offline" + // Optional: to get a refresh token
                          $"&prompt=consent";       // Optional: to force consent screen

            // Open the browser for the user to authenticate
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            Log.Information("Browser opened for Google Sign-In.");
        }

        private async Task<FirebaseAuthResponse?> GetFirebaseUserAsync(string code)
        {
            var url = $"http://localhost:3023/api/tokenFromCode/{Uri.EscapeDataString(code)}";

            using var client = new HttpClient();
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadFromJsonAsync<FirebaseAuthResponse>();
                return jsonResponse;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to get Firebase token: {response.StatusCode}, {errorContent}");
            }
        }

        private async Task RefreshFirebaseTokenAsync(string refreshToken)
        {
            var url = $"http://localhost:3023/api/refreshToken/{Uri.EscapeDataString(refreshToken)}";
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();

                if (jsonResponse == null || string.IsNullOrEmpty(jsonResponse.IdToken))
                {
                    throw new Exception("Firebase ID token not found in the response.");
                }

                if (FirebaseUser == null)
                {
                    throw new InvalidOperationException("Can't refresh token if you're not logged in.");
                }

                FirebaseUser.ExpiresIn = jsonResponse.ExpiresIn;
                FirebaseUser.IdToken = jsonResponse.IdToken;
                FirebaseUser.RefreshToken = jsonResponse.RefreshToken;

            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to refresh Firebase token: {response.StatusCode}, {errorContent}");
            }
        }

        private async Task<string> HandleRedirectAndGetAuthCodeAsync()
        {
            Log.Debug("Listening for the auth redirect...");
            using var listener = new HttpListener();
            listener.Prefixes.Add(_settingsManager.AppSettings.AfmAuthRedirectUri);
            listener.Start();

            try
            {
                // Wait for the redirect
                var context = await listener.GetContextAsync();
                var query = context.Request.QueryString;

                // Extract the authorization code
                var code = query["code"];
                var error = query["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException($"OAuth error: {error}");
                }

                Log.Debug("Received authorization code from the auth redirect.");

                if (string.IsNullOrEmpty(code))
                {
                    throw new InvalidOperationException("Authorization code not found in the redirect.");
                }

                // Send a response back to the browser
                using var response = context.Response;
                string responseString = "Google Sign-In for AFM Data Client successful. You can close this window.";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
                response.OutputStream.Close();

                return code;


            }
            catch (Exception ex)
            {
                // Handle exceptions as needed
                Log.Error($"Error during token handling: {ex.Message}");
                throw;
            }
            finally
            {
                listener.Stop();
            }
        }

        private class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; }

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonPropertyName("scope")]
            public string Scope { get; set; }

            [JsonPropertyName("id_token")]
            public string IdToken { get; set; }
        }

    }
}
