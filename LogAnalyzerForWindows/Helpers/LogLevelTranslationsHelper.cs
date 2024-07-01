using System.Collections.Generic;

namespace LogAnalyzerForWindows.Helpers;

public static class LogLevelTranslationsHelper
{
    public static readonly Dictionary<string, string> LogLevelTranslationsEnglish = new Dictionary<string, string>
    {
        { "Trace", "Trace" },
        { "Debug", "Debug" },
        { "Information", "Information" },
        { "Warning", "Warning" },
        { "Error", "Error" },
        { "Critical", "Critical" }
    };

    public static readonly Dictionary<string, string> LogLevelTranslationsRussian = new Dictionary<string, string>
    {
        { "Trace", "Трассировка" },
        { "Debug", "Отладка" },
        { "Information", "Информация" },
        { "Warning", "Предупреждение" },
        { "Error", "Ошибка" },
        { "Critical", "Критический" }
    };
}