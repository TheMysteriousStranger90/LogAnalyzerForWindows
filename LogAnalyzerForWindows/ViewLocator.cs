using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LogAnalyzerForWindows;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This class is instantiated by Avalonia framework through XAML DataTemplate binding")]
internal sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var name = data.GetType().FullName!
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);

        if (type is not null)
        {
            var control = (Control?)Activator.CreateInstance(type);
            return control;
        }

        return new TextBlock { Text = $"Not Found: {name}" };
    }

    public bool Match(object? data)
    {
        return data is ViewModels.MainWindowViewModel;
    }
}
