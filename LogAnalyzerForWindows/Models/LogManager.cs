using System.Threading.Channels;
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
    private const int ChannelCapacity = 1000;

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

        var logsList = logs as IReadOnlyList<LogEntry> ?? logs.ToList();

        if (logsList.Count == 0) return;

        try
        {
            _analyzer.Analyze(logsList);
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

        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        var consumerCount = Math.Min(Environment.ProcessorCount, 4);
        var consumers = Enumerable.Range(0, consumerCount)
            .Select(_ => ConsumeLogsAsync(channel.Reader, cancellationToken))
            .ToArray();

        try
        {
            foreach (var log in logsList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (log != null)
                {
                    await channel.Writer.WriteAsync(log, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            channel.Writer.Complete();
        }

        await Task.WhenAll(consumers).ConfigureAwait(false);
    }

    private async Task ConsumeLogsAsync(ChannelReader<LogEntry> reader, CancellationToken cancellationToken)
    {
        await foreach (var log in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
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
    }
}
