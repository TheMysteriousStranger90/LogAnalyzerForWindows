using System.Runtime.Versioning;
using Avalonia;
using Avalonia.ReactiveUI;
using LogAnalyzerForWindows.Services;

[assembly: SupportedOSPlatform("windows")]

namespace LogAnalyzerForWindows;

internal sealed class Program
{
    public static SingleInstanceService? SingleInstance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        SingleInstance = new SingleInstanceService();

        if (!SingleInstance.TryStart())
        {
            SingleInstance.Dispose();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
