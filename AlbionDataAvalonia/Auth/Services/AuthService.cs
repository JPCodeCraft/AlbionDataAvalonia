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
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Auth.Services
{
    public class AuthService
    {
        private readonly SettingsManager _settingsManager;
        private FirebaseAuthResponse? _firebaseUser = null;
        public Action<FirebaseAuthResponse?>? FirebaseUserChanged;
        private CancellationTokenSource? _refreshTokenCts;

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

                _firebaseUser = await GetFirebaseUserAsync(code);
                OnFirebaseUserChanged(_firebaseUser);
                ScheduleTokenRefresh();

                Log.Information($"User signed in: {_firebaseUser?.HiddenEmail}");
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

                if (_firebaseUser == null)
                {
                    throw new InvalidOperationException("Can't refresh token if you're not logged in.");
                }

                _firebaseUser.ExpiresIn = jsonResponse.ExpiresIn;
                _firebaseUser.IdToken = jsonResponse.IdToken;
                _firebaseUser.RefreshToken = jsonResponse.RefreshToken;

                OnFirebaseUserChanged(_firebaseUser);
                ScheduleTokenRefresh();

                Log.Information($"Firebase token refreshed for user: {_firebaseUser.Email}");
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

        private void ScheduleTokenRefresh()
        {
            if (_firebaseUser != null && int.TryParse(_firebaseUser.ExpiresIn, out int expiresInSeconds))
            {
                // Calculate 70% of the expiration time
                var delay = TimeSpan.FromSeconds(expiresInSeconds * 0.7);

                // Cancel any existing scheduled refresh
                _refreshTokenCts?.Cancel();

                // Create a new cancellation token source
                _refreshTokenCts = new CancellationTokenSource();

                // Schedule the token refresh
                Task.Delay(delay, _refreshTokenCts.Token)
                    .ContinueWith(async t =>
                    {
                        if (!t.IsCanceled && _firebaseUser != null)
                        {
                            try
                            {
                                await RefreshFirebaseTokenAsync(_firebaseUser.RefreshToken);
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Automatic token refresh failed: {ex.Message}");
                            }
                        }
                    }, TaskScheduler.Default);

                Log.Debug($"Token refresh scheduled in {delay.TotalMinutes} minutes.");
            }
        }

        public void LogOut()
        {
            // Cancel any scheduled token refresh
            _refreshTokenCts?.Cancel();
            _refreshTokenCts = null;

            // Clear the user information
            _firebaseUser = null;
            OnFirebaseUserChanged(_firebaseUser);

            Log.Information("User has been logged out.");
        }

        private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
        {
            FirebaseUserChanged?.Invoke(user);
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
