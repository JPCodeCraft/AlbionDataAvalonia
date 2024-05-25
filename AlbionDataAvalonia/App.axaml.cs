﻿using AlbionDataAvalonia.DB;
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
        CheckAppAlreadyRunning();

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
            await db.Database.MigrateAsync();
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

        //INITIALIZE SETTINGS
        await settings.Initialize();

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

            await ClientUpdater.CheckForUpdatesAsync(settings.AppSettings.LatestVersionUrl, settings.AppSettings.LatesVersionDownloadUrl, settings.AppSettings.FileNameFormat);
        };
        _updateTimer.Start();

        //UPLOADER
        var uploaderCancellationToken = new CancellationTokenSource();
        _ = uploader.ProcessItemsAsync(uploaderCancellationToken.Token).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in uploader, exception: {exception}", t.Exception);
            }
        });

        //LISTENER
        _ = listener.Run().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Error in listener, exception: {exception}", t.Exception);
            }
        });

        //VIEWMODEL
        this.DataContext = vm;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(listSink, restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(logFilePath, LogEventLevel.Debug, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .MinimumLevel.Debug()
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
        collection.AddSingleton<Uploader>();
        collection.AddSingleton<MailService>();

        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<LogsViewModel>();
    }
}
