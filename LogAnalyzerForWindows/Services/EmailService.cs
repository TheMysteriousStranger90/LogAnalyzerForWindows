using System.Diagnostics;
using DotNetEnv;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Mail;

namespace LogAnalyzerForWindows.Services;

internal sealed class EmailService : IEmailService
{
    private readonly EmailSender? _emailSender;

    public EmailService()
    {
        try
        {
            LoadEnvironmentVariables();

            string smtpServer = Env.GetString("SMTP_SERVER");
            int smtpPort = Env.GetInt("SMTP_PORT");
            string fromEmail = Env.GetString("FROM_EMAIL");
            string fromName = Env.GetString("FROM_NAME");
            string password = Env.GetString("SMTP_PASSWORD");

            if (string.IsNullOrWhiteSpace(smtpServer) || smtpPort == 0 ||
                string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(password))
            {
                Debug.WriteLine(
                    "Email settings are not fully configured in .env file. Email sending will be disabled.");
                _emailSender = null;
            }
            else
            {
                _emailSender = new EmailSender(smtpServer, smtpPort, fromEmail, fromName, password);
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine(
                $"IO error loading email settings from .env: {ex.Message}. Email sending will be disabled.");
            _emailSender = null;
        }
        catch (FormatException ex)
        {
            Debug.WriteLine($"Format error in .env file: {ex.Message}. Email sending will be disabled.");
            _emailSender = null;
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine(
                $"Configuration error loading email settings: {ex.Message}. Email sending will be disabled.");
            _emailSender = null;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Debug.WriteLine(
                $"Invalid port configuration: {ex.Message}. Email sending will be disabled.");
            _emailSender = null;
        }
    }

    private static void LoadEnvironmentVariables()
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
            return;
        }

        var projectEnvPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".env");
        if (File.Exists(projectEnvPath))
        {
            Env.Load(projectEnvPath);
            return;
        }

        Env.Load();
    }

    public async Task SendEmailAsync(
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        string attachmentPath)
    {
        if (_emailSender is null)
        {
            Debug.WriteLine("EmailSender is not initialized. Cannot send email. Check .env configuration.");
            throw new InvalidOperationException("Email service is not configured. Please check application settings.");
        }

        try
        {
            await _emailSender.SendEmailAsync(recipientName, recipientEmail, subject, body, attachmentPath)
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
