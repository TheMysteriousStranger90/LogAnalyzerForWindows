using System.Diagnostics;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Mail;

namespace LogAnalyzerForWindows.Services;

internal sealed class EmailService : IEmailService
{
    private readonly ISettingsService _settingsService;

    public EmailService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task SendEmailAsync(
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        string attachmentPath)
    {
        var smtp = _settingsService.GetSmtpSettings();

        if (!_settingsService.IsSmtpConfigured())
        {
            Debug.WriteLine("SMTP is not configured. Cannot send email.");
            throw new InvalidOperationException(
                "Email service is not configured. Please configure SMTP settings in Settings.");
        }

        try
        {
            var emailSender = new EmailSender(
                smtp.Server,
                smtp.Port,
                smtp.FromEmail,
                smtp.FromName,
                smtp.Password);

            await emailSender.SendEmailAsync(recipientName, recipientEmail, subject, body, attachmentPath)
                .ConfigureAwait(false);
            Debug.WriteLine("Email sent successfully.");
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine($"Email sending operation failed: {ex.Message}");
            throw;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"IO error in EmailSender: {ex.Message}");
            throw;
        }
    }
}
