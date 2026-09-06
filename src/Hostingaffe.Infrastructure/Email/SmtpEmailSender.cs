using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Infrastructure.Email;

/// <summary>Sends one message synchronously up to acceptance by the configured SMTP server.</summary>
public sealed class SmtpEmailSender(SmtpSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!settings.Configured)
        {
            throw new InvalidOperationException("SMTP is not configured.");
        }

        var host = settings.Host!;
        var fromAddress = settings.FromAddress!;

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(settings.FromName, fromAddress));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var socket = settings.Security switch
            {
                SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
                SmtpSecurity.Tls => SecureSocketOptions.SslOnConnect,
                SmtpSecurity.None => SecureSocketOptions.None,
                _ => throw new ArgumentOutOfRangeException(),
            };
            await client.ConnectAsync(host, settings.Port, socket, cancellationToken);
            if (settings.Username is not null)
            {
                await client.AuthenticateAsync(settings.Username, settings.Password!, cancellationToken);
            }
            await client.SendAsync(mail, cancellationToken, progress: null);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Transactional email delivery through {SmtpHost} failed.", host);
            throw;
        }
    }
}
