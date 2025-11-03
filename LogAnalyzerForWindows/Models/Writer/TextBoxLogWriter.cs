using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.Models.Writer;

internal sealed class TextBoxLogWriter : ILogWriter
{
    private readonly ILogFormatter _formatter;
    private readonly Action<string> _updateAction;

    public TextBoxLogWriter(ILogFormatter formatter, Action<string> updateAction)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
    }

    public void Write(LogEntry log)
    {
        if (log == null) return;

        string formattedLogString = _formatter.Format(log).ToString();
        _updateAction(formattedLogString);
    }
}
