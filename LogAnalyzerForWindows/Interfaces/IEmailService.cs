using System.Threading.Tasks;

namespace LogAnalyzerForWindows.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string recipientName, string recipientEmail, string subject, string body, string attachmentPath);
}