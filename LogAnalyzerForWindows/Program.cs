using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Avalonia;
using Avalonia.ReactiveUI;
using LogAnalyzerForWindows.Services;

[assembly: SupportedOSPlatform("windows")]

namespace LogAnalyzerForWindows;

internal sealed class Program
{
    public static SingleInstanceService? SingleInstance { get; private set; }
    private static bool IsElevated { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        IsElevated = IsRunningAsAdmin();

        if (!IsElevated && !args.Contains("--no-elevate"))
        {
            if (TryRestartAsAdmin())
            {
                return;
            }
        }

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

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1))
            };

            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
