using System.Collections.Generic;

namespace LogAnalyzerForWindows.Models;

public class LogReport
{
    public string Title { get; set; }
    public string Summary { get; set; }
    public List<string> Details { get; set; }
}