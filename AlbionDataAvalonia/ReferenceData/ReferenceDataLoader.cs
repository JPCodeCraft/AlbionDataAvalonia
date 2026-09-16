using AlbionDataAvalonia.Settings;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ReferenceData;

internal sealed class ReferenceDataLoader
{
    private static readonly AppSettings DefaultSettings = new();
    private readonly HttpClient httpClient;
    private readonly string cacheDirectory;
    private readonly SemaphoreSlim downloadSemaphore = new(3);
    private Func<AppSettings> getAppSettings = () => DefaultSettings;

    public static ReferenceDataLoader Shared { get; } = new(
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
        Path.Combine(AppData.LocalPath, "cache", "reference-data"));

    public ReferenceDataLoader(HttpClient httpClient, string cacheDirectory)
    {
        this.httpClient = httpClient;
        this.cacheDirectory = cacheDirectory;
    }

    public void Configure(Func<AppSettings> getAppSettings)
    {
        ArgumentNullException.ThrowIfNull(getAppSettings);
        this.getAppSettings = getAppSettings;
    }

    // Parse into a separate snapshot: validation must finish before publishing or caching data.
    public async Task<T> LoadAsync<T>(string url, Func<string, T> parse, CancellationToken cancellationToken = default)
    {
        // Snapshot settings for this load; later loads can pick up a remote settings refresh.
        var settings = getAppSettings();
        TimeSpan[] retryDelays =
        [
            GetDuration(settings.ReferenceDataFirstRetryDelaySeconds, DefaultSettings.ReferenceDataFirstRetryDelaySeconds),
            GetDuration(settings.ReferenceDataSecondRetryDelaySeconds, DefaultSettings.ReferenceDataSecondRetryDelaySeconds)
        ];
        var requestTimeout = GetDuration(settings.ReferenceDataRequestTimeoutSeconds, DefaultSettings.ReferenceDataRequestTimeoutSeconds);
        var uri = new Uri(url);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        var cachePath = Path.Combine(cacheDirectory, $"{Path.GetFileName(uri.LocalPath)}.{hash}.cache");

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan? retryAfter = null;
            try
            {
                string content;
                await downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestCancellation.CancelAfter(requestTimeout);
                    using var response = await httpClient.GetAsync(uri, requestCancellation.Token).ConfigureAwait(false);
                    retryAfter = response.Headers.RetryAfter?.Delta;
                    if (response.Headers.RetryAfter?.Date is { } retryDate)
                    {
                        retryAfter = retryDate - DateTimeOffset.UtcNow;
                    }
                    response.EnsureSuccessStatusCode();
                    content = await response.Content.ReadAsStringAsync(requestCancellation.Token).ConfigureAwait(false);
                }
                finally
                {
                    downloadSemaphore.Release();
                }

                var value = parse(content);
                cancellationToken.ThrowIfCancellationRequested();
                await SaveCacheAsync(cachePath, content, cancellationToken).ConfigureAwait(false);
                return value;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 0 && File.Exists(cachePath))
                {
                    try
                    {
                        var cachedContent = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
                        var cachedValue = parse(cachedContent);
                        cancellationToken.ThrowIfCancellationRequested();
                        Log.Warning(ex, "Failed to refresh reference data from {Url}; using the last validated disk cache", url);
                        return cachedValue;
                    }
                    catch (Exception cacheException) when (!cancellationToken.IsCancellationRequested)
                    {
                        Log.Warning(cacheException, "Failed to read or validate reference data cache {CachePath}", cachePath);
                    }
                }

                if (attempt >= retryDelays.Length || !IsTransient(ex))
                {
                    throw;
                }

                var delay = retryDelays[attempt];
                // A longer server cooldown is respected by stopping, not by retrying early
                // or holding startup open for an unbounded amount of time.
                if (retryAfter > retryDelays.Max())
                {
                    throw;
                }
                if (retryAfter > delay)
                {
                    delay = retryAfter.Value;
                }

                Log.Warning(ex, "Reference data unavailable from {Url}; retry {Retry} of {RetryCount} in {DelaySeconds} seconds", url, attempt + 1, retryDelays.Length, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan GetDuration(int seconds, int defaultSeconds)
    {
        // Keep invalid remote settings from causing immediate retries or timer overflows.
        return TimeSpan.FromSeconds(seconds is > 0 and <= int.MaxValue / 1000 ? seconds : defaultSeconds);
    }

    private async Task SaveCacheAsync(string cachePath, string content, CancellationToken cancellationToken)
    {
        var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A read-only or full disk must not discard a successful download.
            Log.Warning(ex, "Failed to save reference data cache {CachePath}", cachePath);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to remove temporary reference data cache {CachePath}", tempPath);
            }
        }
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is OperationCanceledException
            || exception is HttpRequestException requestException
                && (requestException.StatusCode is null
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    || (int)requestException.StatusCode.Value >= 500);
    }
}
