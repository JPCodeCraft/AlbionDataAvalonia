using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Serilog;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Auth.Services
{
    public class AuthService
    {
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;
        private readonly LocalContext _dbContext;

        private bool _isForcingTokenRefresh = false;

        private FirebaseAuthResponse? _firebaseUser = null;
        private CancellationTokenSource? _refreshTokenCts;

        public Action<FirebaseAuthResponse?>? FirebaseUserChanged;

        public string? FirebaseUserId => _firebaseUser?.LocalId;

        public AuthService(SettingsManager settingsManager, PlayerState playerState)
        {
            _settingsManager = settingsManager;
            _playerState = playerState;
            _dbContext = new LocalContext();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            }
        }

        private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    switch (e.Mode)
                    {
                        case PowerModes.Suspend:
                            break;
                        case PowerModes.Resume:
                            Log.Information("System resumed from sleep. Waiting 10 seconds before forcing token refresh.");
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            Log.Information("Forcing token refresh after delay.");
                            await ForceTokenRefreshAsync();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error handling power mode change event");
                }
            }
        }

        public async Task<bool> TryAutoLoginAsync()
        {
            try
            {
                var storedAuth = await _dbContext.UserAuth.FirstOrDefaultAsync();
                if (storedAuth != null && !string.IsNullOrEmpty(storedAuth.RefreshToken))
                {
                    Log.Debug($"Found stored refresh token for user: {storedAuth.UserId}");
                    await RefreshFirebaseTokenAsync(storedAuth.RefreshToken);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Auto-login failed: {ex.Message}");
            }
            return false;
        }

        private async Task StoreRefreshToken(string userId, string refreshToken)
        {
            // Remove any existing tokens
            var existingAuth = await _dbContext.UserAuth.FirstOrDefaultAsync();
            if (existingAuth != null)
            {
                _dbContext.UserAuth.Remove(existingAuth);
            }

            // Store the new token
            var userAuth = new UserAuth
            {
                UserId = userId,
                RefreshToken = refreshToken
            };
            await _dbContext.UserAuth.AddAsync(userAuth);
            await _dbContext.SaveChangesAsync();
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

                // Store the refresh token
                if (_firebaseUser != null)
                {
                    await StoreRefreshToken(_firebaseUser.LocalId, _firebaseUser.RefreshToken);
                }

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
            var url = $"{_settingsManager.AppSettings.AfmAuthApiUrl}/tokenFromCode";
            var query = $"?code={Uri.EscapeDataString(code)}";

            using var client = new HttpClient();
            var response = await client.GetAsync(url + query);

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
            var url = $"{_settingsManager.AppSettings.AfmAuthApiUrl}/refreshToken";
            var query = $"?refreshToken={Uri.EscapeDataString(refreshToken)}";

            using var client = new HttpClient();
            var response = await client.GetAsync(url + query);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();

                if (jsonResponse == null || string.IsNullOrEmpty(jsonResponse.IdToken))
                {
                    throw new Exception("Firebase ID token not found in the response.");
                }

                if (_firebaseUser == null)
                {
                    _firebaseUser = new FirebaseAuthResponse
                    {
                        LocalId = jsonResponse.UserId,
                        Email = jsonResponse.FirebaseDecodedToken.Email,
                        FullName = jsonResponse.FirebaseDecodedToken.Name,
                        PhotoUrl = jsonResponse.FirebaseDecodedToken.Picture,
                        EmailVerified = jsonResponse.FirebaseDecodedToken.EmailVerified,
                        IdToken = jsonResponse.IdToken,
                        RefreshToken = jsonResponse.RefreshToken,
                        ExpiresIn = jsonResponse.ExpiresIn
                    };
                }
                else
                {
                    _firebaseUser.IdToken = jsonResponse.IdToken;
                    _firebaseUser.RefreshToken = jsonResponse.RefreshToken;
                    _firebaseUser.ExpiresIn = jsonResponse.ExpiresIn;
                }

                // Store the new refresh token
                await StoreRefreshToken(_firebaseUser.LocalId, jsonResponse.RefreshToken);

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

        public async Task ForceTokenRefreshAsync()
        {
            if (_firebaseUser == null || string.IsNullOrEmpty(_firebaseUser.RefreshToken))
            {
                Log.Debug("Cannot force token refresh: No user is logged in or refresh token is missing.");
                return;
            }

            if (_isForcingTokenRefresh)
            {
                Log.Debug("Token refresh is already in progress. Canceled.");
                return;
            }

            Log.Information("Forcing token refresh...");
            try
            {
                _isForcingTokenRefresh = true;
                await RefreshFirebaseTokenAsync(_firebaseUser.RefreshToken);
                _isForcingTokenRefresh = false;
            }
            catch (Exception ex)
            {
                _isForcingTokenRefresh = false;
                Log.Error($"Forced token refresh failed: {ex.Message}");
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

        public async Task LogOut()
        {
            // Cancel any scheduled token refresh
            _refreshTokenCts?.Cancel();
            _refreshTokenCts = null;

            // Clear the user information
            _firebaseUser = null;

            _playerState.UploadToAfmOnly = false;

            // Clear the table
            var userAuths = await _dbContext.UserAuth.ToListAsync();
            _dbContext.UserAuth.RemoveRange(userAuths);
            await _dbContext.SaveChangesAsync();
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
            public string? AccessToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("token_type")]
            public string? TokenType { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }

            [JsonPropertyName("id_token")]
            public string? IdToken { get; set; }
        }

    }

}
