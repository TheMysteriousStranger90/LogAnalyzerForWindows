using Avalonia.Controls;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ITrayIconService
{
    void Initialize(Window mainWindow);
    void ShowTrayIcon();
    void HideTrayIcon();
    void MinimizeToTray();
    void RestoreFromTray();
    void ShowWindow();
    void UpdateToolTip(string text);
    event Action? ShowWindowRequested;
    event Action? ExitRequested;
}
