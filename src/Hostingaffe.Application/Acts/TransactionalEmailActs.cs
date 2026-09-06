using System.Net;
using System.Net.Mail;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

public sealed record SmtpStatus(bool Configured, string? Host, int? Port, string? Security, string? Sender);

/// <summary>The templates owned by the application rather than by the SMTP adapter.</summary>
public static class TransactionalEmailTemplates
{
    public static EmailMessage Test(string to) => new(
        to,
        "hostingaffe test email",
        "This is a test email from hostingaffe. Transactional email is configured correctly.\n",
        "<p>This is a test email from <strong>hostingaffe</strong>.</p><p>Transactional email is configured correctly.</p>");

    public static EmailMessage Invitation(string to, string name, Uri link) => Link(
        to, "You are invited to hostingaffe", name,
        "You have been invited to hostingaffe. Set your password using this link:", link);

    public static EmailMessage PasswordRecovery(string to, string name, Uri link) => Link(
        to, "Reset your hostingaffe password", name,
        "Reset your hostingaffe password using this link:", link);

    public static EmailMessage EmailConfirmation(string to, string name, Uri link) => Link(
        to, "Confirm your hostingaffe email address", name,
        "Confirm your new email address using this link:", link);

    private static EmailMessage Link(string to, string subject, string name, string instruction, Uri link)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeLink = WebUtility.HtmlEncode(link.AbsoluteUri);
        return new(to, subject,
            $"Hello {name},\n\n{instruction}\n\n{link.AbsoluteUri}\n",
            $"<p>Hello {safeName},</p><p>{WebUtility.HtmlEncode(instruction)}</p><p><a href=\"{safeLink}\">Continue to hostingaffe</a></p>");
    }
}

public sealed class ReadSmtpStatus(ICallerIdentity callerIdentity, SmtpSettings settings)
{
    public SmtpStatus Execute()
    {
        callerIdentity.Caller.RequireAdministrator("inspect SMTP configuration");
        var security = settings.Configured
            ? settings.Security switch
            {
                SmtpSecurity.StartTls => "starttls",
                SmtpSecurity.Tls => "tls",
                SmtpSecurity.None => "none",
                _ => throw new ArgumentOutOfRangeException(),
            }
            : null;
        return new(settings.Configured, settings.Host, settings.Configured ? settings.Port : null,
            security, settings.Sender);
    }
}

public sealed class SendTestEmail(ICallerIdentity callerIdentity, SmtpSettings settings, IEmailSender sender)
{
    public async Task ExecuteAsync(string? email, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("send a test email");
        if (!settings.Configured)
        {
            throw new Refusal(RefusalCode.SmtpNotConfigured, "Transactional email is not configured for this instance.");
        }

        if (!MailAddress.TryCreate(email, out var address))
        {
            throw Refusal.Validation("email", "An email address is required until user email is persisted.");
        }

        await sender.SendAsync(TransactionalEmailTemplates.Test(address.Address), cancellationToken);
    }
}
