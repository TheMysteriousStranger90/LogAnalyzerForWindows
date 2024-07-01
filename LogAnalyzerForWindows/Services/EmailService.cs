using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LogAnalyzerForWindows.Interfaces;
using LogAnalyzerForWindows.Mail;
using LogAnalyzerForWindows.Models;

namespace LogAnalyzerForWindows.Services;

public class EmailService : IEmailService
{
    public static EmailSender EmailSender { get; set; }
    public async Task SendEmailAsync(string recipientName, string recipientEmail, string subject, string body, string attachmentPath)
    {
        try
        {
            if (EmailSender != null)
            {
                await EmailSender.SendEmailAsync(recipientName, recipientEmail, subject, body, attachmentPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"An error occurred: {ex.Message}");
        }
    }
}