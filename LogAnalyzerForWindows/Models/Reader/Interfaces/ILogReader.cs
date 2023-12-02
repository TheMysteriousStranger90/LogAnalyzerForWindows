using System.Collections.Generic;

namespace LogAnalyzerForWindows.Models.Reader.Interfaces;

public interface ILogReader
{
    IEnumerable<LogEntry> ReadLogs();
}