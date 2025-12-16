using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace LogAnalyzerForWindows.Services;

[SupportedOSPlatform("windows")]
internal sealed partial class SingleInstanceService : IDisposable
{
    private const string MutexName = "Global\\AzioEventLogAnalyzer_SingleInstance";
    private const string PipeName = "AzioEventLogAnalyzer_ActivationPipe";

    private Mutex? _mutex;
    private bool _disposed;
    private bool _ownsMutex;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int dwProcessId);

    private const int SW_RESTORE = 9;
    private const int ASFW_ANY = -1;

    public bool TryStart()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);

            if (!_ownsMutex)
            {
                Debug.WriteLine("Another instance is already running. Activating existing window...");
                ActivateExistingWindow();
                return false;
            }

            Debug.WriteLine("Single instance acquired successfully.");
            return true;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            Debug.WriteLine("Acquired abandoned mutex from crashed instance.");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error acquiring mutex: {ex.Message}");
            return true;
        }
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);

            var currentProcessName = Process.GetCurrentProcess().ProcessName;
            var processes = Process.GetProcessesByName(currentProcessName);

            foreach (var process in processes)
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    var hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                    {
                        if (IsIconic(hWnd))
                        {
                            ShowWindow(hWnd, SW_RESTORE);
                        }

                        SetForegroundWindow(hWnd);

                        Debug.WriteLine($"Activated existing window (PID: {process.Id})");
                        break;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error activating existing window: {ex.Message}");
        }
    }

    private static void SendActivationSignal()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);

            var message = Encoding.UTF8.GetBytes("ACTIVATE");
            client.Write(message, 0, message.Length);

            Debug.WriteLine("Activation signal sent via named pipe.");
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("Named pipe connection timed out.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error sending activation signal: {ex.Message}");
        }
    }

    public void StartListeningForActivation(Action onActivationRequested)
    {
        ArgumentNullException.ThrowIfNull(onActivationRequested);

        Task.Run(async () =>
        {
            while (!_disposed)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);

                    var buffer = new byte[256];
                    var bytesRead = await server.ReadAsync(buffer).ConfigureAwait(false);
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (message == "ACTIVATE")
                    {
                        Debug.WriteLine("Activation signal received.");
                        onActivationRequested();
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in activation listener: {ex.Message}");
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_ownsMutex && _mutex != null)
            {
                _mutex.ReleaseMutex();
                Debug.WriteLine("Mutex released.");
            }
        }
        catch (ApplicationException ex)
        {
            Debug.WriteLine($"Mutex already released: {ex.Message}");
        }
        finally
        {
            _mutex?.Dispose();
            _mutex = null;
            _disposed = true;
        }
    }
}
