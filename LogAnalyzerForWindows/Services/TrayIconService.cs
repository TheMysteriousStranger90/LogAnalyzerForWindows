using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LogAnalyzerForWindows.Interfaces;

namespace LogAnalyzerForWindows.Services;

internal sealed class TrayIconService : ITrayIconService, IDisposable
{
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private Window? _mainWindow;
    private bool _isInitialized;
    private bool _disposed;

    public event Action? ShowWindowRequested;
    public event Action? ExitRequested;

    public void Initialize(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (_isInitialized) return;

        _mainWindow = mainWindow;

        try
        {
            CreateTrayIcon();
            _isInitialized = true;
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Failed to initialize tray icon (invalid operation): {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"Failed to initialize tray icon (not supported): {ex.Message}");
        }
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new NativeMenu();

        var showMenuItem = new NativeMenuItem("Show Log Analyzer");
        showMenuItem.Click += (_, _) => ShowWindow();
        _trayMenu.Add(showMenuItem);

        _trayMenu.Add(new NativeMenuItemSeparator());

        var exitMenuItem = new NativeMenuItem("Exit");
        exitMenuItem.Click += (_, _) => ExitApplication();
        _trayMenu.Add(exitMenuItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Log Analyzer for Windows",
            Menu = _trayMenu,
            IsVisible = false
        };

        LoadTrayIconImage();

        _trayIcon.Clicked += OnTrayIconClicked;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            TrayIcon.SetIcons(Application.Current, [_trayIcon]);
        }
    }

    private void LoadTrayIconImage()
    {
        if (_trayIcon is null) return;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon.Icon = new WindowIcon(iconPath);
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to load tray icon (IO error): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Failed to load tray icon (access denied): {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"Failed to load tray icon (invalid path): {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            Debug.WriteLine($"Failed to load tray icon (not supported): {ex.Message}");
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    public void ShowTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = true;
        }
    }

    public void HideTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    public void MinimizeToTray()
    {
        if (_mainWindow is null) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _mainWindow.Hide();
            ShowTrayIcon();
        });
    }

    public void ShowWindow()
    {
        if (_mainWindow is null) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            HideTrayIcon();
            ShowWindowRequested?.Invoke();
        });
    }

    private void ExitApplication()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            HideTrayIcon();
            ExitRequested?.Invoke();

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    public void UpdateToolTip(string text)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = text;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayMenu = null;
        _mainWindow = null;
        _disposed = true;
    }
}
