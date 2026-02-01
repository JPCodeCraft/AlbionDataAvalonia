using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.ViewModels;
using AlbionDataAvalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AlbionDataAvalonia;

public partial class App : Application
{
    private System.Timers.Timer? _updateTimer;

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
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        //MIGRATIONS
        using (var db = new LocalContext())
        {
            try
            {
                await db.Database.MigrateAsync();
                Log.Information("Migrations [if any] completed successfully");
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in migrations, exception: {exception}", e);
                Log.Information("Deleting database and trying again");
                await db.DeleteDatabase();
                await db.Database.MigrateAsync();
            }
        }

        //DI SETUP
        var collection = new ServiceCollection();
        collection.AddCommonServices();
        var services = collection.BuildServiceProvider();

        //LOGGING
        SetupLogging(services.GetRequiredService<ListSink>());

        //GETTING SERVICES
        vm = services.GetRequiredService<MainViewModel>();
        var settings = services.GetRequiredService<SettingsManager>();
        var listener = services.GetRequiredService<NetworkListenerService>();
        var uploader = services.GetRequiredService<Uploader>();
        var afmUploader = services.GetRequiredService<AFMUploader>();
        var localization = services.GetRequiredService<LocalizationService>();
        var itemsIdsService = services.GetRequiredService<ItemsIdsService>();
        var achievementsService = services.GetRequiredService<AchievementsService>();
        var idleService = services.GetRequiredService<IdleService>();
        var authService = services.GetRequiredService<AuthService>();

        //INITIALIZE SETTINGS
        await settings.InitializeSettings();

        //AUTH SERVICE
        await authService.TryAutoLoginAsync();

        //UPDATER (Windows only)
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
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

                await ClientUpdater.CheckForUpdatesAsync(settings.AppSettings.LatestVersionUrl, settings.AppSettings.LatesVersionDownloadUrl, settings.AppSettings.FileNameFormat);
            };
            _updateTimer.Start();
        }
        else
        {
            Log.Information("Auto-updater is disabled on this OS.");
        }

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

        //LISTENER
        _ = listener.StartNetworkListeningAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in listener, exception: {exception}", t.Exception);
            }
        });

        //IDLE SERVICE
        _ = idleService.ExecuteAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in idle service, exception: {exception}", t.Exception);
            }
        });

        //INITIALIZE LOCALIZATION
        await localization.InitializeAsync();

        //INITIALIZE ITEMS IDS
        await itemsIdsService.InitializeAsync();

        //INITIALIZE ACHIEVEMENTS
        await achievementsService.InitializeAsync();

        //INITIALIZE LOCATIONS
        await AlbionLocations.InitializeAsync();

        //VIEWMODEL
        this.DataContext = vm;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // On macOS, quit when last window closes; elsewhere keep explicit shutdown to allow tray behavior
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            }
            else
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            if (desktop.MainWindow == null)
            {
                desktop.MainWindow = new MainWindow(settings);
                desktop.MainWindow.DataContext = vm;
            }

            if (!settings.UserSettings.StartHidden || !NpCapInstallationChecker.IsNpCapInstalled())
            {
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
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

    private void SetupLogging(ListSink listSink)
    {
        string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMDataClient", "logs", "log-.txt");

        var listSinkLevelSwitch = new Serilog.Core.LoggingLevelSwitch();
        listSinkLevelSwitch.MinimumLevel = LogEventLevel.Information;

        AppData.ListSinkLevelSwitch = listSinkLevelSwitch;

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(listSink, levelSwitch: listSinkLevelSwitch, restrictedToMinimumLevel: LogEventLevel.Verbose)
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
        collection.AddSingleton<LocalizationService>();
        collection.AddSingleton<ItemsIdsService>();
        collection.AddSingleton<AchievementsService>();
        collection.AddSingleton<AuthService>();
        collection.AddSingleton<CsvExportService>();

        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<LogsViewModel>();
        collection.AddSingleton<MailsViewModel>();
        collection.AddSingleton<TradesViewModel>();
    }
}
