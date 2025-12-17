using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LogAnalyzerForWindows.Views;

internal sealed partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
