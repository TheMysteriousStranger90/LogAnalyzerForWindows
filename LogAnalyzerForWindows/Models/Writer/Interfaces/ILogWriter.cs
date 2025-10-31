namespace LogAnalyzerForWindows.Models.Writer.Interfaces;

internal interface ILogWriter
{
    void Write(LogEntry log);
}
