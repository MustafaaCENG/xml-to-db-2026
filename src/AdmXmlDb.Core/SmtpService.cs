using System.Runtime.Versioning;
using AdmXmlDb.Core.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AdmXmlDb.Core;

/// <summary>
/// Centralized SMTP sending via MailKit.
/// Supports anonymous relay (port 25, no credentials) and authenticated SMTP (port 587/465).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SmtpService
{
    private const int TimeoutMs = 30_000;

    /// <summary>
    /// Sends an email. Throws on failure.
    /// </summary>
    public static async Task SendAsync(
        SmtpSettings smtp,
        string[] to,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var message = BuildMessage(smtp.SenderEmail, to, subject, body);
        await SendCoreAsync(smtp, message, ct);
    }

    /// <summary>
    /// Sends a test email (from → to self). Returns error message or null on success.
    /// </summary>
    public static async Task<string?> TestAsync(SmtpSettings smtp, CancellationToken ct = default)
    {
        try
        {
            var message = BuildMessage(
                smtp.SenderEmail,
                [smtp.SenderEmail],
                "AdmXmlDb Test",
                "Test email from AdmXmlDb.");
            await SendCoreAsync(smtp, message, ct);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static async Task SendCoreAsync(SmtpSettings smtp, MimeMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient { Timeout = TimeoutMs };

        // UseSsl=false → plain (port 25 anonymous relay)
        // UseSsl=true  → Auto (MailKit picks SslOnConnect for 465, StartTls for 587)
        var socketOptions = smtp.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
        await client.ConnectAsync(smtp.Host, smtp.Port, socketOptions, ct);

        if (!string.IsNullOrEmpty(smtp.Username))
        {
            var password = DataProtectionHelper.Unprotect(smtp.EncryptedPassword);
            try
            {
                await client.AuthenticateAsync(smtp.Username, password, ct);
            }
            finally
            {
                password = null; // clear from memory
            }
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    private static MimeMessage BuildMessage(string from, string[] to, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        foreach (var addr in to)
            message.To.Add(MailboxAddress.Parse(addr));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }
}
