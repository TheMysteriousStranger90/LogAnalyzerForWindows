using MailKit.Net.Smtp;
using MimeKit;

namespace LogAnalyzerForWindows.Mail;

internal sealed class EmailSender
{
    private readonly string _smtpServer;
    private readonly int _smtpPort;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _password;

    public EmailSender(string smtpServer, int smtpPort, string fromEmail, string fromName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(smtpServer);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (smtpPort <= 0 || smtpPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(smtpPort), "SMTP port must be between 1 and 65535.");
        }

        _smtpServer = smtpServer;
        _smtpPort = smtpPort;
        _fromEmail = fromEmail;
        _fromName = fromName ?? fromEmail;
        _password = password;
    }

    public async Task SendEmailAsync(
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        string? attachmentPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);

        using var message = new MimeMessage();

        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(recipientName ?? recipientEmail, recipientEmail));
        message.Subject = subject ?? "Log Report";

        var builder = new BodyBuilder
        {
            TextBody = body ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
        {
            try
            {
                await builder.Attachments.AddAsync(attachmentPath).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new IOException($"Failed to attach file: {attachmentPath}", ex);
            }
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, MailKit.Security.SecureSocketOptions.StartTls)
                .ConfigureAwait(false);

            await client.AuthenticateAsync(_fromEmail, _password)
                .ConfigureAwait(false);

            await client.SendAsync(message)
                .ConfigureAwait(false);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            throw new InvalidOperationException("SMTP authentication failed. Check email credentials.", ex);
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex)
        {
            throw new InvalidOperationException($"SMTP command failed: {ex.Message}", ex);
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
        {
            throw new IOException($"SMTP protocol error: {ex.Message}", ex);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true).ConfigureAwait(false);
            }
        }
    }
}
