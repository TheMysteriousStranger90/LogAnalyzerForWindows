using System;
using System.Collections.Generic;
using System.Management;
using LogAnalyzerForWindows.Models.Reader.Interfaces;

namespace LogAnalyzerForWindows.Models.Reader;

public class WindowsEventLogReader : ILogReader
{
    private string _logName;

    public WindowsEventLogReader(string logName)
    {
        _logName = logName;
    }

    public IEnumerable<LogEntry> ReadLogs()
    {
        var logs = new List<LogEntry>();

        string query = $"SELECT * FROM Win32_NTLogEvent WHERE Logfile = '{_logName}'";
        using (var searcher = new ManagementObjectSearcher(query))
        {
            foreach (ManagementObject mo in searcher.Get())
            {
                var logEntry = new LogEntry
                {
                    Timestamp = mo["TimeGenerated"] != null
                        ? ManagementDateTimeConverter.ToDateTime(mo["TimeGenerated"].ToString())
                        : DateTime.MinValue,
                    Level = mo["Type"]?.ToString(),
                    Message = mo["Message"]?.ToString()
                };

                logs.Add(logEntry);
            }
        }

        return logs;
    }
}