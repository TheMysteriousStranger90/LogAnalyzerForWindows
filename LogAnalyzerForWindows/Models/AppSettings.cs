namespace LogAnalyzerForWindows.Models;

internal sealed class AppSettings
{
    public SmtpSettings Smtp { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
}
