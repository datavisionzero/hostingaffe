namespace Hostingaffe.Application.Ports;

/// <summary>A transactional message with distinct plain-text and HTML bodies.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>The one outgoing-mail door used by identity acts and administration.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
