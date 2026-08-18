using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Serilog;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

        private FirebaseAuthResponse? _firebaseUser = null;
        private CancellationTokenSource? _refreshTokenCts;

        private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
        private DateTimeOffset? _tokenExpiryUtc;
        private static readonly TimeSpan TokenRefreshLeadTime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MinScheduledRefreshDelay = TimeSpan.FromSeconds(30);
        private const int MinLoopbackPort = 49152;
        private const int MaxLoopbackPortExclusive = 65536;
        private const int LoopbackBindAttempts = 10;

        public Action<FirebaseAuthResponse?>? FirebaseUserChanged;

        public string? FirebaseUserId => _firebaseUser?.LocalId;

        public FirebaseAuthResponse? CurrentFirebaseUser => _firebaseUser;

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

        public async Task<bool> TryAutoLoginAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var storedAuth = await _dbContext.UserAuth.FirstOrDefaultAsync(cancellationToken);
                if (storedAuth != null && !string.IsNullOrEmpty(storedAuth.RefreshToken))
                {
                    Log.Debug($"Found stored refresh token for user: {storedAuth.UserId}");
                    await RefreshFirebaseTokenAsync(storedAuth.RefreshToken, cancellationToken);
                    return true;
                }
            }
            catch (AuthServiceException ex) when (ex.IsInvalidRefreshToken)
            {
                Log.Warning("Stored refresh token rejected during auto-login. Initiating logout.");
                await LogOut();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-login failed");
            }
            return false;
        }

        public async Task<bool> EnsureValidTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (_firebaseUser == null || string.IsNullOrEmpty(_firebaseUser.RefreshToken))
            {
                return false;
            }

            if (!forceRefresh && !ShouldRefreshToken())
            {
                return true;
            }

            await _tokenRefreshLock.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && !ShouldRefreshToken())
                {
                    return true;
                }

                await RefreshFirebaseTokenAsync(_firebaseUser.RefreshToken, cancellationToken);
                return true;
            }
            catch (AuthServiceException ex) when (ex.IsInvalidRefreshToken)
            {
                Log.Warning("Token refresh rejected by server ({StatusCode}). Logging out user.", ex.StatusCode);
                await LogOut();
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Token refresh failed");
                return false;
            }
            finally
            {
                _tokenRefreshLock.Release();
            }
        }

        public async Task<bool> TryRecoverFromUnauthorizedAsync(CancellationToken cancellationToken = default)
        {
            if (_firebaseUser == null)
            {
                return false;
            }

            var refreshed = await EnsureValidTokenAsync(forceRefresh: true, cancellationToken);
            if (!refreshed)
            {
                Log.Warning("Failed to recover from unauthorized response. User may need to sign in again.");
            }

            return refreshed;
        }

        private async Task StoreRefreshToken(string userId, string refreshToken, CancellationToken cancellationToken = default)
        {
            // Remove any existing tokens
            var existingAuth = await _dbContext.UserAuth.FirstOrDefaultAsync(cancellationToken);
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
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task SignInAsync(Action<Uri> authorizationReady, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(authorizationReady);
            Log.Debug("Starting sign in...");

            using var listener = StartLoopbackListener(out var redirectUri);
            var state = CreateRandomBase64UrlValue();
            var codeVerifier = CreateRandomBase64UrlValue();
            var codeChallenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
            var authorizationUri = BuildGoogleSignInUri(redirectUri, state, codeChallenge);

            try
            {
                authorizationReady(authorizationUri);
                var code = await HandleRedirectAndGetAuthCodeAsync(listener, state, cancellationToken);
                listener.Stop();

                var authResponse = await GetFirebaseUserAsync(code, redirectUri, codeVerifier, cancellationToken);

                if (authResponse == null
                    || string.IsNullOrWhiteSpace(authResponse.LocalId)
                    || string.IsNullOrWhiteSpace(authResponse.IdToken)
                    || string.IsNullOrWhiteSpace(authResponse.RefreshToken))
                {
                    throw new AuthServiceException("Firebase sign-in returned an incomplete response.");
                }

                await StoreRefreshToken(authResponse.LocalId, authResponse.RefreshToken, cancellationToken);
                UpdateFirebaseUser(authResponse);

                OnFirebaseUserChanged(_firebaseUser);
                ScheduleTokenRefresh();

                Log.Information($"User signed in: {_firebaseUser?.HiddenEmail}");
            }
            catch (OperationCanceledException)
            {
                Log.Information("Sign-in was canceled or timed out.");
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Sign-in failed");
                throw;
            }
            finally
            {
                listener.Close();
            }
        }

        private string BuildGoogleSignInUrl(Uri redirectUri, string state, string codeChallenge)
        {
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                          $"?client_id={Uri.EscapeDataString(_settingsManager.AppSettings.AfmAuthClientId)}" +
                          $"&redirect_uri={Uri.EscapeDataString(redirectUri.AbsoluteUri)}" +
                          $"&response_type=code" +
                          $"&scope=openid%20email%20profile" +
                          $"&access_type=offline" +
                          $"&prompt=consent" +
                          $"&state={Uri.EscapeDataString(state)}" +
                          $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                          $"&code_challenge_method=S256";

            return authUrl;
        }

        private Uri BuildGoogleSignInUri(Uri redirectUri, string state, string codeChallenge)
        {
            return new Uri(BuildGoogleSignInUrl(redirectUri, state, codeChallenge), UriKind.Absolute);
        }

        private async Task<FirebaseAuthResponse?> GetFirebaseUserAsync(
            string code,
            Uri redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_settingsManager.AppSettings.AfmAuthApiUrl}/tokenFromCode";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await client.PostAsJsonAsync(url, new
            {
                code,
                redirectUri = redirectUri.AbsoluteUri,
                codeVerifier
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadFromJsonAsync<FirebaseAuthResponse>(cancellationToken: cancellationToken);
                return jsonResponse;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw AuthServiceException.TokenExchangeError(response.StatusCode, errorContent);
            }
        }

        private async Task RefreshFirebaseTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            var url = $"{_settingsManager.AppSettings.AfmAuthApiUrl}/refreshToken";
            var query = $"?refreshToken={Uri.EscapeDataString(refreshToken)}";

            using var client = new HttpClient();
            var response = await client.GetAsync(url + query, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(cancellationToken: cancellationToken);

                if (jsonResponse == null || string.IsNullOrEmpty(jsonResponse.IdToken))
                {
                    throw new AuthServiceException("Firebase ID token not found in the refresh response.");
                }

                UpdateFirebaseUser(jsonResponse);
                await StoreRefreshToken(_firebaseUser!.LocalId, jsonResponse.RefreshToken, cancellationToken);

                OnFirebaseUserChanged(_firebaseUser);
                ScheduleTokenRefresh();

                Log.Information($"Firebase token refreshed for user: {_firebaseUser.HiddenEmail}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw AuthServiceException.RefreshTokenError(response.StatusCode, errorContent);
            }
        }

        private void UpdateFirebaseUser(RefreshTokenResponse jsonResponse)
        {
            if (_firebaseUser == null)
            {
                _firebaseUser = new FirebaseAuthResponse
                {
                    LocalId = jsonResponse.UserId,
                    Email = jsonResponse.FirebaseDecodedToken?.Email ?? string.Empty,
                    FullName = jsonResponse.FirebaseDecodedToken?.Name ?? string.Empty,
                    PhotoUrl = jsonResponse.FirebaseDecodedToken?.Picture ?? string.Empty,
                    EmailVerified = jsonResponse.FirebaseDecodedToken?.EmailVerified ?? false,
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

                if (!string.IsNullOrEmpty(jsonResponse.FirebaseDecodedToken?.Email))
                {
                    _firebaseUser.Email = jsonResponse.FirebaseDecodedToken.Email;
                }

                if (!string.IsNullOrEmpty(jsonResponse.FirebaseDecodedToken?.Name))
                {
                    _firebaseUser.FullName = jsonResponse.FirebaseDecodedToken.Name;
                }

                if (!string.IsNullOrEmpty(jsonResponse.FirebaseDecodedToken?.Picture))
                {
                    _firebaseUser.PhotoUrl = jsonResponse.FirebaseDecodedToken.Picture;
                }

                if (jsonResponse.FirebaseDecodedToken is not null)
                {
                    _firebaseUser.EmailVerified = jsonResponse.FirebaseDecodedToken.EmailVerified;
                }
            }

            UpdateTokenExpiry(jsonResponse.ExpiresIn);
        }

        private void UpdateFirebaseUser(FirebaseAuthResponse authResponse)
        {
            _firebaseUser = authResponse;
            UpdateTokenExpiry(authResponse.ExpiresIn);
        }

        private void UpdateTokenExpiry(string? expiresIn)
        {
            if (int.TryParse(expiresIn, out var seconds) && seconds > 0)
            {
                _tokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
            }
            else
            {
                _tokenExpiryUtc = null;
            }
        }

        private bool ShouldRefreshToken()
        {
            if (_tokenExpiryUtc == null)
            {
                return true;
            }

            return DateTimeOffset.UtcNow >= _tokenExpiryUtc.Value - TokenRefreshLeadTime;
        }

        private static HttpListener StartLoopbackListener(out Uri redirectUri)
        {
            HttpListenerException? lastException = null;

            for (var attempt = 0; attempt < LoopbackBindAttempts; attempt++)
            {
                var port = RandomNumberGenerator.GetInt32(MinLoopbackPort, MaxLoopbackPortExclusive);
                var candidateUri = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
                var listener = new HttpListener();
                listener.Prefixes.Add(candidateUri.AbsoluteUri);

                try
                {
                    listener.Start();
                    redirectUri = candidateUri;
                    Log.Debug("Listening for the auth redirect on {RedirectUri}", candidateUri);
                    return listener;
                }
                catch (HttpListenerException ex)
                {
                    lastException = ex;
                    listener.Close();
                    Log.Debug(ex, "Could not bind auth callback listener on port {Port}", port);
                }
            }

            redirectUri = null!;
            throw new AuthServiceException(
                "AFM could not reserve a local sign-in port. Close other AFM instances and try again.",
                innerException: lastException,
                kind: AuthServiceErrorKind.LoopbackUnavailable);
        }

        private static string CreateRandomBase64UrlValue()
        {
            return ToBase64Url(RandomNumberGenerator.GetBytes(32));
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static async Task<string> HandleRedirectAndGetAuthCodeAsync(
            HttpListener listener,
            string expectedState,
            CancellationToken cancellationToken)
        {
            using var cancellationRegistration = cancellationToken.Register(listener.Close);

            try
            {
                while (true)
                {
                    var context = await listener.GetContextAsync();
                    var request = context.Request;
                    var query = request.QueryString;
                    var state = query["state"];

                    if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(request.Url?.AbsolutePath, "/", StringComparison.Ordinal)
                        || !string.Equals(state, expectedState, StringComparison.Ordinal))
                    {
                        await WriteLoopbackResponseAsync(context.Response, HttpStatusCode.BadRequest,
                            "This sign-in request is not valid. Return to AFM Data Client and try again.");
                        continue;
                    }

                    var error = query["error"];
                    if (!string.IsNullOrEmpty(error))
                    {
                        await WriteLoopbackResponseAsync(context.Response, HttpStatusCode.OK,
                            "Google sign-in was canceled. You can close this window and return to AFM Data Client.");
                        throw new AuthServiceException(
                            "Google sign-in was canceled or denied.",
                            kind: AuthServiceErrorKind.AuthorizationDenied);
                    }

                    var code = query["code"];
                    if (string.IsNullOrEmpty(code))
                    {
                        await WriteLoopbackResponseAsync(context.Response, HttpStatusCode.BadRequest,
                            "The authorization code was missing. Return to AFM Data Client and try again.");
                        continue;
                    }

                    Log.Debug("Received authorization code from the auth redirect.");
                    await WriteLoopbackResponseAsync(context.Response, HttpStatusCode.OK,
                        "Sign-in request received. You can close this window and return to AFM Data Client.");
                    return code;
                }
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        private static async Task WriteLoopbackResponseAsync(
            HttpListenerResponse response,
            HttpStatusCode statusCode,
            string message)
        {
            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                response.StatusCode = (int)statusCode;
                response.ContentType = "text/plain; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.Headers[HttpResponseHeader.CacheControl] = "no-store";
                await response.OutputStream.WriteAsync(buffer);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not write the auth callback response to the browser");
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not close the auth callback response");
                }
            }
        }

        public async Task ForceTokenRefreshAsync(CancellationToken cancellationToken = default)
        {
            if (_firebaseUser == null || string.IsNullOrEmpty(_firebaseUser.RefreshToken))
            {
                Log.Debug("Cannot force token refresh: No user is logged in or refresh token is missing.");
                return;
            }

            Log.Information("Forcing token refresh...");
            var refreshed = await EnsureValidTokenAsync(forceRefresh: true, cancellationToken);
            if (!refreshed)
            {
                Log.Warning("Forced token refresh did not succeed.");
            }
        }

        private void ScheduleTokenRefresh()
        {
            _refreshTokenCts?.Cancel();

            if (_firebaseUser == null)
            {
                return;
            }

            TimeSpan delay;

            if (_tokenExpiryUtc.HasValue)
            {
                delay = _tokenExpiryUtc.Value - DateTimeOffset.UtcNow - TokenRefreshLeadTime;
                if (delay < MinScheduledRefreshDelay)
                {
                    delay = MinScheduledRefreshDelay;
                }
            }
            else
            {
                delay = MinScheduledRefreshDelay;
            }

            _refreshTokenCts = new CancellationTokenSource();

            Task.Delay(delay, _refreshTokenCts.Token)
                .ContinueWith(async t =>
                {
                    if (t.IsCanceled)
                    {
                        return;
                    }

                    try
                    {
                        await EnsureValidTokenAsync(forceRefresh: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Automatic token refresh failed");
                    }
                }, TaskScheduler.Default);

            Log.Debug($"Token refresh scheduled in {delay.TotalMinutes:F1} minutes.");
        }

        public async Task LogOut()
        {
            // Cancel any scheduled token refresh
            _refreshTokenCts?.Cancel();
            _refreshTokenCts = null;
            _tokenExpiryUtc = null;

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
