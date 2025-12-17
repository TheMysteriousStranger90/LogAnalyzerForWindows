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
    private bool _pendingMinimizeToTray;

    public event Action? ShowWindowRequested;
    public event Action? ExitRequested;

    public void Initialize(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (_isInitialized) return;

        _mainWindow = mainWindow;

        if (_mainWindow.IsLoaded)
        {
            CreateTrayIconSafe();
        }
        else
        {
            _mainWindow.Opened += OnMainWindowOpened;
        }

        _isInitialized = true;
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.Opened -= OnMainWindowOpened;
        }

        Dispatcher.UIThread.Post(() =>
        {
            CreateTrayIconSafe();

            if (_pendingMinimizeToTray)
            {
                _pendingMinimizeToTray = false;
                MinimizeToTrayInternal();
            }
        }, DispatcherPriority.Loaded);
    }

    private void CreateTrayIconSafe()
    {
        try
        {
            CreateTrayIcon();
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
        if (_trayIcon is not null) return;

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
            var icons = TrayIcon.GetIcons(Application.Current);
            if (icons is null)
            {
                TrayIcon.SetIcons(Application.Current, new TrayIcons { _trayIcon });
            }
            else if (!icons.Contains(_trayIcon))
            {
                icons.Add(_trayIcon);
            }
        }

        Debug.WriteLine("TrayIcon created successfully");
    }

    private void LoadTrayIconImage()
    {
        if (_trayIcon is null) return;

        try
        {
            var possiblePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "icon.ico"),
                Path.Combine(Environment.CurrentDirectory, "Assets", "icon.ico")
            };

            foreach (var iconPath in possiblePaths)
            {
                if (File.Exists(iconPath))
                {
                    _trayIcon.Icon = new WindowIcon(iconPath);
                    Debug.WriteLine($"Tray icon loaded from: {iconPath}");
                    return;
                }
            }

            try
            {
                var uri = new Uri("avares://LogAnalyzerForWindows/Assets/icon.ico");
                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                if (stream is not null)
                {
                    // For WindowIcon we need a file path, so save to temp
                    var tempPath = Path.Combine(Path.GetTempPath(), "LogAnalyzer_icon.ico");
                    using var fileStream = File.Create(tempPath);
                    stream.CopyTo(fileStream);
                    fileStream.Close();
                    _trayIcon.Icon = new WindowIcon(tempPath);
                    Debug.WriteLine($"Tray icon loaded from embedded resource to: {tempPath}");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load embedded icon: {ex.Message}");
            }

            Debug.WriteLine("Warning: Could not find tray icon file");
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
        if (_trayIcon is null)
        {
            Debug.WriteLine("ShowTrayIcon called but tray icon not yet created");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.IsVisible = true;
                Debug.WriteLine("Tray icon shown");
            }
        });
    }

    public void HideTrayIcon()
    {
        if (_trayIcon is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.IsVisible = false;
                }
            });
        }
    }

    public void MinimizeToTray()
    {
        if (_mainWindow is null) return;

        if (_trayIcon is null)
        {
            _pendingMinimizeToTray = true;
            Debug.WriteLine("MinimizeToTray deferred - tray icon not ready");
            return;
        }

        MinimizeToTrayInternal();
    }

    private void MinimizeToTrayInternal()
    {
        if (_mainWindow is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            ShowTrayIcon();
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Hide();
            Debug.WriteLine("Window minimized to tray");
        }, DispatcherPriority.Background);
    }

    public void RestoreFromTray()
    {
        if (_mainWindow is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            HideTrayIcon();
            Debug.WriteLine("Window restored from tray");
        });
    }

    public void ShowWindow()
    {
        if (_mainWindow is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
            HideTrayIcon();
            ShowWindowRequested?.Invoke();
            Debug.WriteLine("Window shown");
        });
    }

    private void ExitApplication()
    {
        Dispatcher.UIThread.Post(() =>
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
            Dispatcher.UIThread.Post(() =>
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.ToolTipText = text;
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_mainWindow is not null)
        {
            _mainWindow.Opened -= OnMainWindowOpened;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Clicked -= OnTrayIconClicked;
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }

        _trayIcon = null;
        _trayMenu = null;
        _mainWindow = null;
        _disposed = true;
    }
}
