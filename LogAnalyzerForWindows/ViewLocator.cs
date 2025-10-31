using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace LogAnalyzerForWindows;

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
