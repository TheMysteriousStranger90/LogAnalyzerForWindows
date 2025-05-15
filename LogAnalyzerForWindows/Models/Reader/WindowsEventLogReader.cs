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


                        var logEntry = new LogEntry
                        {
                            Timestamp = timestamp,
                            // Type is actually an EventType (e.g., 1 for Error, 2 for Warning, 4 for Information)
                            // This needs mapping to string Level if that's the expectation.
                            // For now, using the raw 'Type' property which might be a number.
                            Level = mo["Type"]
                                ?.ToString(), // This will be the numeric type. Map to "Error", "Warning" etc. if needed.
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