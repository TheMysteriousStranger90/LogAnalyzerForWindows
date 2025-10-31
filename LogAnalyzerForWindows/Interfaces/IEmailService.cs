using System.Threading.Tasks;

namespace LogAnalyzerForWindows.Interfaces;

internal interface IEmailService
{
    /// <summary>
    /// Sends an email with optional attachment.
    /// </summary>
    /// <param name="recipientName">Name of the recipient.</param>
    /// <param name="recipientEmail">Email address of the recipient.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="body">Email body content.</param>
    /// <param name="attachmentPath">Optional path to attachment file.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when email service is not configured.</exception>
    /// <exception cref="IOException">Thrown when there's an IO error during email sending.</exception>
    Task SendEmailAsync(string recipientName, string recipientEmail, string subject, string body, string attachmentPath);
}
