using System.IO;
using System.Xml.Serialization;
using LogAnalyzerForWindows.Models.Formatter.Interfaces;

namespace LogAnalyzerForWindows.Models.Formatter;

public class XmlLogFormatter : ILogFormatter
{
    public LogEntry Format(LogEntry log)
    {
        var serializer = new XmlSerializer(typeof(LogEntry));
        using var stringWriter = new StringWriter();
        serializer.Serialize(stringWriter, log);
        return new LogEntry { Message = stringWriter.ToString() };
    }
}