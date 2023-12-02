namespace LogAnalyzerForWindows.Models.Writer.Interfaces;

public interface ILogWriter
{
    void Write(LogEntry log);
}