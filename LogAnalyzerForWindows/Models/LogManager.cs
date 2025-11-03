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

    public async Task ProcessLogsAsync(IEnumerable<LogEntry> logs, CancellationToken cancellationToken = default)
    {
        if (logs == null) return;

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

        const int batchSize = 100;
        var logsList = logs.ToList();

        for (int i = 0; i < logsList.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = logsList.Skip(i).Take(batchSize);

            foreach (var log in batch)
            {
                if (log == null) continue;

                try
                {
                    var formattedLog = _formatter.Format(log);
                    _writer.Write(formattedLog);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"An error occurred while formatting or writing log: {ex.Message}");
                }
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }
}
