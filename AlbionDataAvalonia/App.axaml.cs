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


        var vm = services.GetRequiredService<MainViewModel>();
        var settings = services.GetRequiredService<SettingsManager>();
        var listener = services.GetRequiredService<NetworkListenerService>();

        await settings.Initialize();
        listener.Run();

        this.DataContext = vm;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!settings.UserSettings.StartHidden)
            {
                desktop.MainWindow = new MainWindow(settings);
                desktop.MainWindow.DataContext = vm;
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
}
public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<NetworkListenerService>();
        collection.AddSingleton<PlayerState>();
        collection.AddSingleton<ConnectionService>();
        collection.AddSingleton<SettingsManager>();


        collection.AddTransient<PowSolver>();
        collection.AddTransient<Uploader>();

        collection.AddTransient<MainViewModel>();
        collection.AddTransient<SettingsViewModel>();
    }
}
