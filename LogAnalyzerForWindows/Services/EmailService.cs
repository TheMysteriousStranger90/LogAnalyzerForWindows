using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Mail;
using DotNetEnv;

namespace LogAnalyzerForWindows.Services;

public class EmailService : IEmailService
{
    private readonly EmailSender _emailSender;

    public EmailService()
    {
        try
        {
            var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
            }
            else
            {
                var projectEnvPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".env");
                if (File.Exists(projectEnvPath))
                {
                     Env.Load(projectEnvPath);
                }
                else
                {
                     Env.Load();
                }
            }


            string smtpServer = Env.GetString("SMTP_SERVER");
            int smtpPort = Env.GetInt("SMTP_PORT");
            string fromEmail = Env.GetString("FROM_EMAIL");
            string fromName = Env.GetString("FROM_NAME");
            string password = Env.GetString("SMTP_PASSWORD");

            if (string.IsNullOrWhiteSpace(smtpServer) || smtpPort == 0 || string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(password))
            {
                Debug.WriteLine("Email settings are not fully configured in .env file. Email sending will be disabled.");
                _emailSender = null;
            }
            else
            {
                _emailSender = new EmailSender(smtpServer, smtpPort, fromEmail, fromName, password);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading email settings from .env: {ex.Message}. Email sending will be disabled.");
            _emailSender = null;
        }
    }

    public async Task SendEmailAsync(string recipientName, string recipientEmail, string subject, string body, string attachmentPath)
    {
        if (_emailSender == null)
        {
            Debug.WriteLine("EmailSender is not initialized. Cannot send email. Check .env configuration.");
            throw new InvalidOperationException("Email service is not configured. Please check application settings.");
        }

        try
        {
            await _emailSender.SendEmailAsync(recipientName, recipientEmail, subject, body, attachmentPath);
            Debug.WriteLine("Email sent successfully.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred within EmailSender: {ex.Message}");
            throw;
        }
    }
}