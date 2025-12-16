using System.Collections.Concurrent;
using System.Management;
using System.Runtime.Versioning;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models.Reader;

[SupportedOSPlatform("windows")]
internal sealed class WindowsEventLogReader : ILogReader
{
    private readonly string _logName;
    private DateTime? _lastReadTime;
    private readonly object _lastReadTimeLock = new();

    public WindowsEventLogReader(string logName)
    {
        if (string.IsNullOrWhiteSpace(logName))
        {
            throw new ArgumentException("Log name cannot be null or whitespace.", nameof(logName));
        }

        _logName = logName;
    }

    public static async Task<List<string>> GetAvailableLogNamesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GetAvailableLogNames(), cancellationToken).ConfigureAwait(false);
    }

    public static List<string> GetAvailableLogNames()
    {
        var logNames = new List<string>();

        try
        {
            string query = "SELECT LogfileName FROM Win32_NTEventlogFile";
            try
            {
                using var searcher = new ManagementObjectSearcher(query);

                foreach (ManagementBaseObject moBase in searcher.Get())
                {
                    using ManagementObject mo = (ManagementObject)moBase;
                    var logName = mo["LogfileName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(logName))
                    {
                        logNames.Add(logName);
                    }
                }
            }
            catch (ManagementException ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI table 'Win32_NTEventlogFile' not found: {ex.Message}");
                return ["System", "Application", "Security"];
            }
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting log names: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Access denied getting log names: {ex.Message}");
        }

        if (logNames.Count == 0)
        {
            logNames.AddRange(["System", "Application", "Security"]);
        }

        return logNames.OrderBy(x => x).ToList();
    }

    public static async Task<List<string>> GetAvailableLevelsForLogAsync(
        string logName,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => GetAvailableLevelsForLog(logName), cancellationToken).ConfigureAwait(false);
    }

    public static List<string> GetAvailableLevelsForLog(string logName)
    {
        var levels = new HashSet<string>();

        try
        {
            string query = $"SELECT Type FROM Win32_NTLogEvent WHERE Logfile = '{logName}'";
            using var searcher = new ManagementObjectSearcher(query);

            int count = 0;
            foreach (ManagementBaseObject moBase in searcher.Get())
            {
                using ManagementObject mo = (ManagementObject)moBase;
                string level = MapEventTypeToLevelString(mo["Type"]);
                levels.Add(level);

                count++;
                if (count > 1000) break;
            }
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting levels for log '{logName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Access denied for log '{logName}': {ex.Message}");
        }

        if (levels.Count == 0)
        {
            return GetDefaultLevelsForLog(logName);
        }

        return levels.OrderBy(x => GetLevelPriority(x)).ToList();
    }

    private static List<string> GetDefaultLevelsForLog(string logName)
    {
        return logName.Equals("Security", StringComparison.OrdinalIgnoreCase)
            ? ["AuditSuccess", "AuditFailure"]
            : ["Error", "Warning", "Information"];
    }

    private static int GetLevelPriority(string level)
    {
        return level switch
        {
            "Error" => 1,
            "Warning" => 2,
            "Information" => 3,
            "AuditFailure" => 4,
            "AuditSuccess" => 5,
            _ => 99
        };
    }

    private static string MapEventTypeToLevelString(object? eventTypeObj)
    {
        if (eventTypeObj == null)
            return "Unknown";

        string? eventTypeStr = eventTypeObj.ToString();

        return eventTypeStr switch
        {
            // Russian
            "Ошибка" => "Error",
            "Предупреждение" => "Warning",
            "Информация" => "Information",
            "Успешный аудит" or "Успех аудита" => "AuditSuccess",
            "Ошибка аудита" or "Отказ аудита" => "AuditFailure",
            // English
            "Error" => "Error",
            "Warning" => "Warning",
            "Information" => "Information",
            "Audit Success" or "Success Audit" => "AuditSuccess",
            "Audit Failure" or "Failure Audit" => "AuditFailure",
            // Numeric
            "1" => "Error",
            "2" => "Warning",
            "3" => "Information",
            "4" => "AuditSuccess",
            "5" => "AuditFailure",
            _ => ParseNumericEventType(eventTypeStr)
        };
    }

    private static string ParseNumericEventType(string? eventTypeStr)
    {
        if (ushort.TryParse(eventTypeStr, out ushort eventTypeNumeric))
        {
            return eventTypeNumeric switch
            {
                1 => "Error",
                2 => "Warning",
                3 => "Information",
                4 => "AuditSuccess",
                5 => "AuditFailure",
                _ => "Other"
            };
        }

        System.Diagnostics.Debug.WriteLine($"Unknown event type: {eventTypeStr}");
        return "Other";
    }

    public IEnumerable<LogEntry> ReadLogs()
    {
        return ReadLogsAsync().GetAwaiter().GetResult();
    }

    public async Task<List<LogEntry>> ReadLogsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ReadLogsInternal(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private List<LogEntry> ReadLogsInternal(CancellationToken cancellationToken)
    {
        var logs = new ConcurrentBag<LogEntry>();

        string timeFilter;
        lock (_lastReadTimeLock)
        {
            timeFilter = _lastReadTime.HasValue
                ? $" AND TimeGenerated > '{_lastReadTime.Value.ToUniversalTime():yyyyMMddHHmmss}.000000+000'"
                : "";
        }

        string query =
            $"SELECT TimeGenerated, Type, Message, EventCode, SourceName FROM Win32_NTLogEvent WHERE Logfile = '{_logName}'{timeFilter}";

        DateTime? maxTimestamp = null;
        var maxTimestampLock = new object();

        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            var managementObjects = searcher.Get().Cast<ManagementObject>().ToList();

            Parallel.ForEach(
                managementObjects,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = cancellationToken
                },
                moBase =>
                {
                    using ManagementObject mo = moBase;

                    DateTime? timestamp = ParseTimestamp(mo["TimeGenerated"]);
                    string textualLevel = MapEventTypeToLevelString(mo["Type"]);
                    int? eventId = ParseEventId(mo["EventCode"]);
                    string? source = mo["SourceName"]?.ToString();

                    var logEntry = new LogEntry
                    {
                        Timestamp = timestamp,
                        Level = textualLevel,
                        Message = mo["Message"]?.ToString(),
                        EventId = eventId,
                        Source = source
                    };

                    logs.Add(logEntry);

                    if (timestamp.HasValue)
                    {
                        lock (maxTimestampLock)
                        {
                            if (!maxTimestamp.HasValue || timestamp > maxTimestamp)
                            {
                                maxTimestamp = timestamp;
                            }
                        }
                    }
                });

            // Update last read time
            if (maxTimestamp.HasValue)
            {
                lock (_lastReadTimeLock)
                {
                    if (!_lastReadTime.HasValue || maxTimestamp > _lastReadTime)
                    {
                        _lastReadTime = maxTimestamp;
                    }
                }
            }
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WMI Query failed for log '{_logName}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Access denied for log '{_logName}': {ex.Message}");
        }

        return logs.ToList();
    }

    private static DateTime? ParseTimestamp(object? timeGeneratedValue)
    {
        if (timeGeneratedValue == null)
            return DateTime.MinValue;

        try
        {
            return ManagementDateTimeConverter.ToDateTime(timeGeneratedValue.ToString());
        }
        catch (FormatException)
        {
            return DateTime.MinValue;
        }
    }

    private static int? ParseEventId(object? eventCodeValue)
    {
        if (eventCodeValue != null && int.TryParse(eventCodeValue.ToString(), out int parsedEventId))
        {
            return parsedEventId;
        }

        return null;
    }
}
