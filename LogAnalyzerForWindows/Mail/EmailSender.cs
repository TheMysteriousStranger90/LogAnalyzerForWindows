using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LogAnalyzerForWindows.Models;

public class EmailSender
{
    private string _smtpServer;
    private int _smtpPort;
    private string _fromEmail;
    private string _fromName;
    private string _password;

    public EmailSender(string smtpServer, int smtpPort, string fromEmail, string fromName, string password)
    {
        _smtpServer = smtpServer;
        _smtpPort = smtpPort;
        _fromEmail = fromEmail;
        _fromName = fromName;
        _password = password;
    }

    public async Task SendEmailAsync(string toName, string toEmail, string subject, string body, string attachmentPath)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(_fromName, _fromEmail));
        email.To.Add(new MailboxAddress(toName, toEmail));
        email.Subject = subject;

        var bodyPart = new TextPart("plain") { Text = body };
        var attachmentPart = new MimePart("application", "zip")
        {
            Content = new MimeContent(File.OpenRead(attachmentPath)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = Path.GetFileName(attachmentPath)
        };

        var multipart = new Multipart("mixed");
        multipart.Add(bodyPart);
        multipart.Add(attachmentPart);

        email.Body = multipart;

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_fromEmail, _password);
            await client.SendAsync(email);
            await client.DisconnectAsync(true);
        }
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            email = Regex.Replace(email, @"(@)(.+)$", DomainMapper, RegexOptions.None, TimeSpan.FromMilliseconds(200));

            static string DomainMapper(Match match)
            {
                var idn = new IdnMapping();
                var domainName = idn.GetAscii(match.Groups[2].Value);
                return match.Groups[1].Value + domainName;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(email,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}