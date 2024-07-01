using System;
using System.Collections.Generic;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Reader.Interfaces;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.Models;

public class LogManager : ILogManager
{
    private ILogReader _reader;
    private LogAnalyzer _analyzer;
    private ILogFormatter _formatter;
    private ILogWriter _writer;

    public LogManager(ILogReader reader, LogAnalyzer analyzer, ILogFormatter formatter, ILogWriter writer)
    {
        _reader = reader;
        _analyzer = analyzer;
        _formatter = formatter;
        _writer = writer;
    }

    public void ProcessLogs(IEnumerable<LogEntry> logs)
    {
        try
        {
            _analyzer.Analyze(logs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while analyzing logs: {ex.Message}");
            return;
        }

        try
        {
            foreach (var log in logs)
            {
                var formattedLog = _formatter.Format(log);
                _writer.Write(formattedLog);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while formatting or writing logs: {ex.Message}");
        }
    }
}