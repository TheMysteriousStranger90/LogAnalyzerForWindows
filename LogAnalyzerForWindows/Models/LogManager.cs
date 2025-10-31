using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzerForWindows.Formatter.Interfaces;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Models.Analyzer;
using LogAnalyzerForWindows.Models.Reader.Interfaces;
using LogAnalyzerForWindows.Models.Writer.Interfaces;

namespace LogAnalyzerForWindows.Models;

internal sealed class LogManager : ILogManager
{
    private readonly ILogReader _reader;
    private readonly LogAnalyzer _analyzer;
    private readonly ILogFormatter _formatter;
    private readonly ILogWriter _writer;

    public LogManager(ILogReader reader, LogAnalyzer analyzer, ILogFormatter formatter, ILogWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void ProcessLogs(IEnumerable<LogEntry> logs)
    {
        if (logs == null)
        {
            return;
        }

        try
        {
            _analyzer.Analyze(logs);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"An error occurred while analyzing logs: {ex.Message}");
            return;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"An error occurred while analyzing logs: {ex.Message}");
            return;
        }

        var logsList = logs.ToList();

        foreach (var log in logsList)
        {
            if (log == null) continue;

            try
            {
                var formattedLog = _formatter.Format(log);
                _writer.Write(formattedLog);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"An error occurred while formatting or writing log (Timestamp: {log.Timestamp}, Level: {log.Level}): {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"An error occurred while formatting or writing log (Timestamp: {log.Timestamp}, Level: {log.Level}): {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"An error occurred while formatting or writing log (Timestamp: {log.Timestamp}, Level: {log.Level}): {ex.Message}");
            }
        }
    }
}
