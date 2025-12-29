using Avalonia;
using Serilog;
using System;
using System.IO;

namespace AlbionDataAvalonia.Desktop.Linux;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            TryWriteStartupCrashLog(e);
            Log.Fatal(e, "Global Exception Handler.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void TryWriteStartupCrashLog(Exception exception)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AFMDataClient",
                "logs");
            Directory.CreateDirectory(logDir);
            File.WriteAllText(Path.Combine(logDir, "startup-crash.txt"), exception.ToString());
        }
        catch
        {
        }
    }
}
