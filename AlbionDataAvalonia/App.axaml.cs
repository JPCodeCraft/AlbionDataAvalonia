using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.ViewModels;
using AlbionDataAvalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia;

public partial class App : Application
{
    private System.Timers.Timer? _updateTimer;
    private readonly HashSet<string> _shownManualUpdateDialogs = new();
    private readonly object _shownManualUpdateDialogsLock = new();

    MainViewModel? vm;

    public override void Initialize()
    {
        try
        {
            CheckAppAlreadyRunning();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check whether app is already running.");
        }

        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            await OnFrameworkInitializationCompletedAsync();
        }
        catch (Exception ex)
        {
            TryWriteStartupCrashLog(ex);

            try
            {
                Log.Fatal(ex, "Application startup failed.");
                Log.CloseAndFlush();
            }
            catch
            {
            }

            ShutdownApplication();
        }
    }

    private async Task OnFrameworkInitializationCompletedAsync()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        //DI SETUP
        var collection = new ServiceCollection();
        collection.AddCommonServices();
        var services = collection.BuildServiceProvider();
        LazyItemImage.Configure(services.GetRequiredService<ItemImageService>());

        //LOGGING
        SetupLogging(services.GetRequiredService<ListSink>());

        //MIGRATIONS
        using (var db = new LocalContext())
        {
            try
            {
                var knownMigrations = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
                var unknownAppliedMigrations = (await db.Database.GetAppliedMigrationsAsync())
                    .Where(migration => !knownMigrations.Contains(migration))
                    .ToArray();

                if (unknownAppliedMigrations.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"The local database was upgraded by a newer application version. " +
                        $"This version cannot safely use it. Unknown migrations: {string.Join(", ", unknownAppliedMigrations)}");
                }

                await db.Database.MigrateAsync();
                Log.Information("Migrations [if any] completed successfully");
            }
            catch (Exception e)
            {
                Log.Fatal(e, "Local database migration failed. Keeping the existing database untouched.");
                throw;
            }
        }

        //GETTING SERVICES
        var settings = services.GetRequiredService<SettingsManager>();
        var listener = services.GetRequiredService<NetworkListenerService>();
        var uploader = services.GetRequiredService<Uploader>();
        var afmUploader = services.GetRequiredService<AFMUploader>();
        var emvBackendLoader = services.GetRequiredService<ItemEstimatedMarketValueBackendLoader>();
        var mobsService = services.GetRequiredService<MobsService>();
        var itemsIdsService = services.GetRequiredService<ItemsIdsService>();
        var achievementsService = services.GetRequiredService<AchievementsService>();
        var idleService = services.GetRequiredService<IdleService>();
        var authService = services.GetRequiredService<AuthService>();
        var gatheringTracker = services.GetRequiredService<GatheringTrackerService>();

        //INITIALIZE SETTINGS
        await settings.InitializeSettings();

        //AUTH SERVICE
        await authService.TryAutoLoginAsync();

        //UPDATER
        _updateTimer = new System.Timers.Timer
        {
            AutoReset = true,
            Interval = TimeSpan.FromMinutes(settings.AppSettings.FirstUpdateCheckDelayMins).TotalMilliseconds, // Delay for the first run
            Enabled = true
        };
        _updateTimer.Elapsed += async (sender, e) =>
        {
            // Change the interval to one hour after the first run
            _updateTimer.Interval = TimeSpan.FromHours(settings.AppSettings.UpdateCheckIntervalHours).TotalMilliseconds;

            var updateResult = await ClientUpdater.CheckForUpdatesAsync(
                settings.AppSettings.LatestVersionUrl,
                settings.AppSettings.LatesVersionDownloadUrl,
                settings.AppSettings.FileNameFormat,
                settings.UserSettings.JoinBetaProgram);

            if (!updateResult.UpdateAvailable || updateResult.Update == null)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                await ClientUpdater.InstallUpdateAsync(updateResult.Update);
                return;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                await ShowManualUpdateDialogOnceAsync(updateResult.Update);
            }
        };
        _updateTimer.Start();

        //INITIALIZE MOBS
        await mobsService.InitializeAsync();

        //UPLOADER
        var uploaderCancellationToken = new CancellationTokenSource();
        _ = uploader.ProcessItemsAsync(uploaderCancellationToken.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in uploader, exception: {exception}", t.Exception);
            }
        });

        //AFM UPLOADER
        afmUploader.Initialize();
        emvBackendLoader.Initialize();

        //IDLE SERVICE
        _ = idleService.ExecuteAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in idle service, exception: {exception}", t.Exception);
            }
        });

        //INITIALIZE ITEMS IDS
        await itemsIdsService.InitializeAsync();

        //INITIALIZE ACHIEVEMENTS
        await achievementsService.InitializeAsync();

        //INITIALIZE LOCATIONS
        await AlbionLocations.InitializeAsync();

        //RESTORE GATHERING SESSION BEFORE PACKETS ARRIVE
        await gatheringTracker.InitializeSessionRecoveryAsync();

        //VIEWMODEL
        vm = services.GetRequiredService<MainViewModel>();

        //LISTENER
        _ = listener.StartNetworkListeningAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in listener, exception: {exception}", t.Exception);
            }
        });

        this.DataContext = vm;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (desktop.MainWindow == null)
            {
                desktop.MainWindow = new MainWindow(settings);
                desktop.MainWindow.DataContext = vm;
            }

            if (!NpCapInstallationChecker.IsNpCapInstalled())
            {
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
            }
            else
            {
                switch (settings.UserSettings.StartupWindowMode)
                {
                    case StartupWindowMode.ShowMainWindow:
                        desktop.MainWindow.Show();
                        desktop.MainWindow.Activate();
                        break;
                    case StartupWindowMode.MinimizedToTaskbar:
                        if (desktop.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.ShowMinimizedToTaskbar();
                        }
                        else
                        {
                            desktop.MainWindow.Show();
                            desktop.MainWindow.WindowState = WindowState.Minimized;
                        }

                        break;
                    case StartupWindowMode.HiddenToTray:
                        break;
                }
            }
        }

        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();

    }

    private Task ShowManualUpdateDialogOnceAsync(ClientUpdateInfo update)
    {
        var updateKey = $"{update.Channel}:{update.Version}";
        lock (_shownManualUpdateDialogsLock)
        {
            if (!_shownManualUpdateDialogs.Add(updateKey))
            {
                return Task.CompletedTask;
            }
        }

        Log.Warning(
            "There's a new {Channel} version available, but automatic updates are not supported on this platform. Please update manually.",
            update.Channel);

        Dispatcher.UIThread.Post(() => _ = ShowManualUpdateDialogAsync(update));
        return Task.CompletedTask;
    }

    private async Task ShowManualUpdateDialogAsync(ClientUpdateInfo update)
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                await UpdateAvailableWindow.ShowAsync(desktop.MainWindow, update);
                return;
            }

            Log.Warning("Could not show update dialog because the current application lifetime is not desktop.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show update dialog.");
        }
    }

    private static void TryWriteStartupCrashLog(Exception exception)
    {
        try
        {
            var logDir = Path.Combine(AppData.LocalPath, "logs");
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "startup-crash.txt"), exception.ToString());
        }
        catch
        {
        }
    }

    private static void ShutdownApplication()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Environment.Exit(1);
    }

    private void SetupLogging(ListSink listSink)
    {
        string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMDataClient", "logs", "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(listSink, restrictedToMinimumLevel: LogEventLevel.Verbose)
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(logFilePath, LogEventLevel.Debug, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .MinimumLevel.Verbose()
            .CreateLogger();
    }
    public void OnTrayClicked(object sender, EventArgs e)
    {
        vm?.ShowMainWindow();
    }

    private void CheckAppAlreadyRunning()
    {
        var currentProcess = Process.GetCurrentProcess();
        var runningProcess = Process.GetProcesses().FirstOrDefault(p => p.Id != currentProcess.Id && p.ProcessName.Equals(currentProcess.ProcessName, StringComparison.Ordinal));

        if (runningProcess != null)
        {
            currentProcess.Kill();
        }
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<NetworkListenerService>();
        collection.AddSingleton<PlayerState>();
        collection.AddSingleton<ConnectionService>();
        collection.AddSingleton<SettingsManager>();
        collection.AddSingleton<ListSink>();
        collection.AddSingleton<IdleService>();
        collection.AddSingleton<Uploader>();
        collection.AddSingleton<AFMUploader>();
        collection.AddSingleton<MailService>();
        collection.AddSingleton<TradeService>();
        collection.AddSingleton<PortfolioUploadService>();
        collection.AddSingleton<MobsService>();
        collection.AddSingleton<ItemsIdsService>();
        collection.AddSingleton<ItemImageService>();
        collection.AddSingleton<ItemEstimatedMarketValueService>();
        collection.AddSingleton<ItemEstimatedMarketValueBackendLoader>();
        collection.AddSingleton<AchievementsService>();
        collection.AddSingleton<AuthService>();
        collection.AddSingleton<CsvExportService>();
        collection.AddSingleton<PartyTrackerService>();
        collection.AddSingleton<CombatTrackerService>();
        collection.AddSingleton<GatheringSessionPersistenceService>();
        collection.AddSingleton<GatheringTrackerService>();
        collection.AddSingleton<LootTrackerService>();
        collection.AddSingleton<LegendaryDefinitionsService>();
        collection.AddSingleton<LegendaryItemTrackerService>();
        collection.AddSingleton<LegendarySaleService>();
        collection.AddSingleton<WindowsStartupService>();

        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<LogsViewModel>();
        collection.AddSingleton<MailsViewModel>();
        collection.AddSingleton<TradesViewModel>();
        collection.AddSingleton<CombatViewModel>();
        collection.AddSingleton<GatheringViewModel>();
        collection.AddSingleton<LootViewModel>();
        collection.AddSingleton<LegendaryViewModel>();
    }
}
