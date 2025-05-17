using System;
using System.Collections.Generic;
using System.Management;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models.Reader;

public class WindowsEventLogReader : ILogReader
{
    private readonly string _logName;

    public WindowsEventLogReader(string logName)
    {
        if (string.IsNullOrWhiteSpace(logName))
        {
            throw new ArgumentException("Log name cannot be null or whitespace.", nameof(logName));
        }

        _logName = logName;
    }
    
    private string MapEventTypeToLevelString(object eventTypeObj)
    {
        if (eventTypeObj == null)
            return "Unknown";

        string eventTypeStr = eventTypeObj.ToString();
        
        switch (eventTypeStr)
        {
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

        string query = $"SELECT TimeGenerated, Type, Message FROM Win32_NTLogEvent WHERE Logfile = '{_logName}'";

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
                            catch (FormatException ex)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"Failed to parse TimeGenerated '{timeGeneratedValue}': {ex.Message}");
                                timestamp = DateTime.MinValue;
                            }
                        }
                        else
                        {
                            timestamp = DateTime.MinValue;
                        }

                        string textualLevel = MapEventTypeToLevelString(mo["Type"]);
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Read log: {timestamp}, {textualLevel}, {mo["Message"]}");

                        var logEntry = new LogEntry
                        {
                            Timestamp = timestamp,
                            Level = textualLevel,
                            Message = mo["Message"]?.ToString()
                        };
                        logs.Add(logEntry);
                    }
                }
            }
        }
        catch (ManagementException ex)
        {
            System.Diagnostics.Debug.WriteLine($"WMI Query failed for log '{_logName}': {ex.Message}");
        }
        return logs;
    }
}