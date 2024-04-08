using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Network.Pow;
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
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace AlbionDataAvalonia;

public partial class App : Application
{

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);

        // Register all the services needed for the application to run
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        // Creates a ServiceProvider containing services from the provided IServiceCollection
        var services = collection.BuildServiceProvider();

        SetupLogging(services.GetRequiredService<ListSink>());

        var vm = services.GetRequiredService<MainViewModel>();
        var settings = services.GetRequiredService<SettingsManager>();
        var listener = services.GetRequiredService<NetworkListenerService>();

        await settings.Initialize();

        _ = ClientUpdater.CheckForUpdatesAsync(settings.AppSettings.LatestVersionUrl, settings.AppSettings.LatesVersionDownloadUrl, settings.AppSettings.FileNameFormat);

        listener.Run();

        this.DataContext = vm;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            desktop.MainWindow = new MainWindow(settings);
            desktop.MainWindow.DataContext = vm;

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
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(listSink, restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.Console()
            .WriteTo.Debug()
            .MinimumLevel.Debug()
            .CreateLogger();
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


        collection.AddTransient<PowSolver>();
        collection.AddTransient<Uploader>();

        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<SettingsViewModel>();
        collection.AddSingleton<LogsViewModel>();
    }
}
