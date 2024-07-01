using System;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.Models.Writer;

public class TextBoxLogWriter : ILogWriter
{
    private ILogFormatter _formatter;
    private Action<string> _updateAction;

    public TextBoxLogWriter(ILogFormatter formatter, Action<string> updateAction)
    {
        _formatter = formatter;
        _updateAction = updateAction;
    }

    public void Write(LogEntry log)
    {
        string formattedLog = _formatter.Format(log).ToString();
        _updateAction(formattedLog);
    }
}