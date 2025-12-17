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
#pragma warning disable CA1861
            string query = "SELECT LogfileName FROM Win32_NTEventlogFile";
#pragma warning restore CA1861
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
#pragma warning disable CA1861
            string query = $"SELECT Type, EventType FROM Win32_NTLogEvent WHERE Logfile = '{logName}'";
#pragma warning restore CA1861
            using var searcher = new ManagementObjectSearcher(query);

            int count = 0;
            foreach (ManagementBaseObject moBase in searcher.Get())
            {
                using ManagementObject mo = (ManagementObject)moBase;

                string level = ParseNumericEventType(mo["EventType"])
                               ?? MapEventTypeToLevelString(mo["Type"]);
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

    private static string? ParseNumericEventType(object? eventTypeValue)
    {
        if (eventTypeValue == null)
            return null;

        if (byte.TryParse(eventTypeValue.ToString(), out byte eventType))
        {
            return eventType switch
            {
                1 => "Error",
                2 => "Warning",
                3 => "Information",
                4 => "AuditSuccess",
                5 => "AuditFailure",
                _ => null
            };
        }

        return null;
    }

    private static string MapEventTypeToLevelString(object? eventTypeObj)
    {
        if (eventTypeObj == null)
            return "Unknown";

        string? eventTypeStr = eventTypeObj.ToString()?.Trim();

        if (string.IsNullOrEmpty(eventTypeStr))
            return "Unknown";

        if (ushort.TryParse(eventTypeStr, out ushort numericType))
        {
            return numericType switch
            {
                1 => "Error",
                2 => "Warning",
                3 or 4 => "Information",
                8 => "AuditSuccess",
                16 => "AuditFailure",
                _ => "Other"
            };
        }

        string? result = eventTypeStr switch
        {
            // Error variants
            "Error" or "Ошибка" or "Fehler" or "Erreur" or "Erro" or "Errore" or "Fout" or "Błąd" or "Chyba"
                or "Fel" => "Error",

            // Warning variants
            "Warning" or "Предупреждение" or "Warnung" or "Avertissement" or "Advertencia" or "Aviso" or "Avviso"
                or "Waarschuwing" or "Ostrzeżenie" or "Upozornění" or "Varning" => "Warning",

            // Information variants
            "Information" or "Информация" or "Сведения" or "Informationen" or "Información" or "Informação"
                or "Informações" or "Informazione" or "Informazioni" or "Informatie" or "Informacja"
                or "Informace" => "Information",

            // AuditSuccess variants
            "Audit Success" or "Success Audit" or "Успешный аудит" or "Успех аудита" or "Erfolgsüberwachung"
                or "Erfolgreiche Überwachung" or "Audit des succès" or "Succès de l'audit" or "Auditoría correcta"
                or "Éxito de auditoría" or "Auditoria com êxito" or "Êxito de auditoria" or "Controllo riuscito"
                or "Operazioni riuscite" or "Geslaagde controle" or "Inspekcja zakończona powodzeniem"
                or "Úspěšný audit" or "Lyckad granskning" => "AuditSuccess",

            // AuditFailure variants
            "Audit Failure" or "Failure Audit" or "Ошибка аудита" or "Отказ аудита" or "Fehlerüberwachung"
                or "Fehlgeschlagene Überwachung" or "Audit des échecs" or "Échec de l'audit" or "Error de auditoría"
                or "Auditoría errónea" or "Falha de auditoria" or "Auditoria com falha" or "Controllo non riuscito"
                or "Operazioni non riuscite" or "Mislukte controle" or "Inspekcja zakończona niepowodzeniem"
                or "Neúspěšný audit" or "Misslyckad granskning" => "AuditFailure",

            _ => null
        };

        if (result != null)
            return result;

        return FuzzyMatchEventType(eventTypeStr);
    }

#pragma warning disable CA1307 // Specify StringComparison for clarity
#pragma warning disable CA1304 // Specify CultureInfo
#pragma warning disable CA1308 // Normalize stringsto uppercase
    private static string FuzzyMatchEventType(string eventTypeStr)
    {
        string normalized = eventTypeStr.ToLowerInvariant();

        bool isAudit = normalized.Contains("audit") ||
                       normalized.Contains("аудит") ||
                       normalized.Contains("überwach") ||
                       normalized.Contains("contrôle") ||
                       normalized.Contains("auditoría") ||
                       normalized.Contains("auditoria") ||
                       normalized.Contains("controllo") ||
                       normalized.Contains("controle") ||
                       normalized.Contains("inspekcj") ||
                       normalized.Contains("granskning") ||
                       normalized.Contains("审核") ||
                       normalized.Contains("監査") ||
                       normalized.Contains("감사");

        if (isAudit)
        {
            if (normalized.Contains("success") ||
                normalized.Contains("успех") ||
                normalized.Contains("erfolg") ||
                normalized.Contains("succès") ||
                normalized.Contains("éxito") ||
                normalized.Contains("êxito") ||
                normalized.Contains("riuscit") ||
                normalized.Contains("geslaagd") ||
                normalized.Contains("powodzeniem") ||
                normalized.Contains("úspěšn") ||
                normalized.Contains("lyckad") ||
                normalized.Contains("成功") ||
                normalized.Contains("성공"))
            {
                return "AuditSuccess";
            }

            if (normalized.Contains("fail") ||
                normalized.Contains("отказ") ||
                normalized.Contains("ошибка") ||
                normalized.Contains("fehl") ||
                normalized.Contains("échec") ||
                normalized.Contains("error") ||
                normalized.Contains("falha") ||
                normalized.Contains("non riuscit") ||
                normalized.Contains("mislukt") ||
                normalized.Contains("niepowodzeniem") ||
                normalized.Contains("neúspěšn") ||
                normalized.Contains("misslyckad") ||
                normalized.Contains("失败") ||
                normalized.Contains("실패"))
            {
                return "AuditFailure";
            }
        }

        if (normalized.Contains("error") ||
            normalized.Contains("ошибка") ||
            normalized.Contains("fehler") ||
            normalized.Contains("erreur") ||
            normalized.Contains("errore") ||
            normalized.Contains("erro") ||
            normalized.Contains("fout") ||
            normalized.Contains("błąd") ||
            normalized.Contains("chyba") ||
            normalized.Contains("fel") ||
            normalized.Contains("错误") ||
            normalized.Contains("エラー") ||
            normalized.Contains("오류"))
        {
            return "Error";
        }

        if (normalized.Contains("warning") ||
            normalized.Contains("предупреждение") ||
            normalized.Contains("warnung") ||
            normalized.Contains("avertissement") ||
            normalized.Contains("advertencia") ||
            normalized.Contains("avviso") ||
            normalized.Contains("aviso") ||
            normalized.Contains("waarschuwing") ||
            normalized.Contains("ostrzeżenie") ||
            normalized.Contains("upozornění") ||
            normalized.Contains("varning") ||
            normalized.Contains("警告") ||
            normalized.Contains("경고"))
        {
            return "Warning";
        }

        if (normalized.Contains("information") ||
            normalized.Contains("информация") ||
            normalized.Contains("сведения") ||
            normalized.Contains("informationen") ||
            normalized.Contains("información") ||
            normalized.Contains("informação") ||
            normalized.Contains("informazione") ||
            normalized.Contains("informatie") ||
            normalized.Contains("informacja") ||
            normalized.Contains("informace") ||
            normalized.Contains("信息") ||
            normalized.Contains("情報") ||
            normalized.Contains("정보"))
        {
            return "Information";
        }

        System.Diagnostics.Debug.WriteLine(
            $"[WindowsEventLogReader] Unknown event type: '{eventTypeStr}' - please report for localization support");
        return "Other";
    }
#pragma warning restore CA1304
#pragma warning restore CA1307
#pragma warning restore CA1308

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

#pragma warning disable CA1861
        string query =
            $"SELECT TimeGenerated, Type, EventType, Message, EventCode, SourceName FROM Win32_NTLogEvent WHERE Logfile = '{_logName}'{timeFilter}";
#pragma warning restore CA1861

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

                    string textualLevel = ParseNumericEventType(mo["EventType"])
                                          ?? MapEventTypeToLevelString(mo["Type"]);

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
