using System;
using System.Collections.Generic;
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
                return "AuditSuccess";
            case "Ошибка аудита":
                return "AuditFailure";
            // English
            case "Error":
                return "Error";
            case "Warning":
                return "Warning";
            case "Information":
                return "Information";
            case "Audit Success":
                return "AuditSuccess";
            case "Audit Failure":
                return "AuditFailure";
        }

        if (ushort.TryParse(eventTypeStr, out ushort eventTypeNumeric))
        {
            switch (eventTypeNumeric)
            {
                case 1: return "Error";
                case 2: return "Warning";
                case 4: return "Information";
                case 8: return "AuditSuccess";
                case 16: return "AuditFailure";
            }
        }

        return "Other";
    }

    public IEnumerable<LogEntry> ReadLogs()
    {
        var logs = new List<LogEntry>();

        string timeFilter = "";
        if (_lastReadTime.HasValue)
        {
            string wmiTime = _lastReadTime.Value.ToUniversalTime().ToString("yyyyMMddHHmmss.000000+000", CultureInfo.InvariantCulture);
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
