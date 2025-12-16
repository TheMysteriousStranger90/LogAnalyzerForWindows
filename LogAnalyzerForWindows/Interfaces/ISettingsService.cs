using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Interfaces;

internal interface ISettingsService
{
    AppSettings GetSettings();
    Task SaveSettingsAsync(AppSettings settings);
    SmtpSettings GetSmtpSettings();
    GeneralSettings GetGeneralSettings();
    bool IsSmtpConfigured();
}
