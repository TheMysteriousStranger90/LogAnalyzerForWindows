using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models.Reader;

[SupportedOSPlatform("windows")]
internal sealed class WindowsEventLogReader : ILogReader
{
    private readonly string _logName;
    private DateTime? _lastReadTime;

    public WindowsEventLogReader(string logName)
    {
        if (string.IsNullOrWhiteSpace(logName))
        {
            throw new ArgumentException("Log name cannot be null or whitespace.", nameof(logName));
        }

        _logName = logName;
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
                return new List<string> { "System", "Application", "Security" };
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
            var defaultLogNames = new[] { "System", "Application", "Security" };
            logNames.AddRange(defaultLogNames);
        }

        return logNames.OrderBy(x => x).ToList();
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
            ? new List<string> { "AuditSuccess", "AuditFailure" }
            : new List<string> { "Error", "Warning", "Information" };
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

    private static string MapEventTypeToLevelString(object eventTypeObj)
    {
        if (eventTypeObj == null)
            return "Unknown";

        string? eventTypeStr = eventTypeObj.ToString();

        switch (eventTypeStr)
        {
            // Russian
            case "Ошибка":
                return "Error";
            case "Предупреждение":
                return "Warning";
            case "Информация":
                return "Information";
            case "Успешный аудит":
            case "Успех аудита":
                return "AuditSuccess";
            case "Ошибка аудита":
            case "Отказ аудита":
                return "AuditFailure";
            // English
            case "Error":
                return "Error";
            case "Warning":
                return "Warning";
            case "Information":
                return "Information";
            case "Audit Success":
            case "Success Audit":
                return "AuditSuccess";
            case "Audit Failure":
            case "Failure Audit":
                return "AuditFailure";
        }

        if (ushort.TryParse(eventTypeStr, out ushort eventTypeNumeric))
        {
            switch (eventTypeNumeric)
            {
                case 1: return "Error";
                case 2: return "Warning";
                case 3: return "Information";
                case 4: return "AuditSuccess";
                case 5: return "AuditFailure";
            }
        }

        System.Diagnostics.Debug.WriteLine($"Unknown event type: {eventTypeStr}");
        return "Other";
    }

    public IEnumerable<LogEntry> ReadLogs()
    {
        var logs = new List<LogEntry>();

        string timeFilter = "";
        if (_lastReadTime.HasValue)
        {
            string wmiTime = _lastReadTime.Value.ToUniversalTime()
                .ToString("yyyyMMddHHmmss.000000+000", CultureInfo.InvariantCulture);
            timeFilter = $" AND TimeGenerated > '{wmiTime}'";
        }

        string query =
            $"SELECT TimeGenerated, Type, Message FROM Win32_NTLogEvent WHERE Logfile = '{_logName}'{timeFilter}";

        try
        {
            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementBaseObject moBase in searcher.Get())
                {
                    using (ManagementObject mo = (ManagementObject)moBase)
                    {
                        DateTime? timestamp = null;
                        var timeGeneratedValue = mo["TimeGenerated"];
                        if (timeGeneratedValue != null)
                        {
                            try
                            {
                                timestamp = ManagementDateTimeConverter.ToDateTime(timeGeneratedValue.ToString());
                            }
                            catch (FormatException)
                            {
                                timestamp = DateTime.MinValue;
                            }
                        }
                        else
                        {
                            timestamp = DateTime.MinValue;
                        }

                        string textualLevel = MapEventTypeToLevelString(mo["Type"]);

                        var logEntry = new LogEntry
                        {
                            Timestamp = timestamp,
                            Level = textualLevel,
                            Message = mo["Message"]?.ToString()
                        };
                        logs.Add(logEntry);

                        if (timestamp.HasValue && (!_lastReadTime.HasValue || timestamp > _lastReadTime))
                            _lastReadTime = timestamp;
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

        return logs;
    }
}
