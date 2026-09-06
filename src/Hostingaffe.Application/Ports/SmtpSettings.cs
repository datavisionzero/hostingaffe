using System.Net.Mail;

namespace Hostingaffe.Application.Ports;

public enum SmtpSecurity
{
    StartTls,
    Tls,
    None,
}

/// <summary>The operator-owned SMTP configuration, validated once at startup.</summary>
public sealed record SmtpSettings(
    string? Host,
    int Port,
    string? Username,
    string? Password,
    SmtpSecurity Security,
    string? FromAddress,
    string FromName,
    Uri? PublicUrl)
{
    public const string HostVariable = "HOSTINGAFFE_SMTP_HOST";
    public const string PortVariable = "HOSTINGAFFE_SMTP_PORT";
    public const string UsernameVariable = "HOSTINGAFFE_SMTP_USERNAME";
    public const string PasswordVariable = "HOSTINGAFFE_SMTP_PASSWORD";
    public const string SecurityVariable = "HOSTINGAFFE_SMTP_SECURITY";
    public const string FromAddressVariable = "HOSTINGAFFE_SMTP_FROM_ADDRESS";
    public const string FromNameVariable = "HOSTINGAFFE_SMTP_FROM_NAME";
    public const string PublicUrlVariable = "HOSTINGAFFE_PUBLIC_URL";

    public bool Configured => Host is not null;

    public string? Sender => Configured ? $"{FromName} <{FromAddress}>" : null;

    public static SmtpSettings FromVariables(
        string? host, string? port, string? username, string? password,
        string? security, string? fromAddress, string? fromName,
        string? publicUrl, bool development)
    {
        var hasHost = !string.IsNullOrWhiteSpace(host);
        var related = new[] { port, username, password, security, fromAddress };
        if (!hasHost && related.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException($"{HostVariable} is required when another SMTP variable is set.", HostVariable);
        }

        Uri? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out baseUrl)
                || baseUrl.Scheme is not ("http" or "https")
                || baseUrl.Query.Length > 0 || baseUrl.Fragment.Length > 0
                || baseUrl.AbsolutePath != "/" || publicUrl.EndsWith('/'))
            {
                throw new ArgumentException($"{PublicUrlVariable} has to be an absolute http(s) origin without a trailing slash.", PublicUrlVariable);
            }

            if (!development && baseUrl.Scheme != "https")
            {
                throw new ArgumentException($"{PublicUrlVariable} has to use https outside Development.", PublicUrlVariable);
            }
        }

        if (!hasHost)
        {
            return new(null, 587, null, null, SmtpSecurity.StartTls, null,
                string.IsNullOrWhiteSpace(fromName) ? "hostingaffe" : fromName.Trim(), baseUrl);
        }

        var chosenPort = string.IsNullOrWhiteSpace(port) ? 587
            : int.TryParse(port, out var parsedPort) && parsedPort is > 0 and <= 65535
                ? parsedPort
                : throw new ArgumentException($"{PortVariable} has to be a port from 1 through 65535.", PortVariable);

        var chosenSecurity = (string.IsNullOrWhiteSpace(security) ? "starttls" : security.Trim().ToLowerInvariant()) switch
        {
            "starttls" => SmtpSecurity.StartTls,
            "tls" => SmtpSecurity.Tls,
            "none" when development => SmtpSecurity.None,
            "none" => throw new ArgumentException($"{SecurityVariable}=none is allowed only in Development.", SecurityVariable),
            _ => throw new ArgumentException($"{SecurityVariable} is one of starttls, tls or none.", SecurityVariable),
        };

        var hasUsername = !string.IsNullOrWhiteSpace(username);
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        if (hasUsername != hasPassword)
        {
            throw new ArgumentException($"{UsernameVariable} and {PasswordVariable} go together.",
                hasUsername ? PasswordVariable : UsernameVariable);
        }

        if (!MailAddress.TryCreate(fromAddress, out var sender))
        {
            throw new ArgumentException($"{FromAddressVariable} has to be an email address.", FromAddressVariable);
        }

        if (baseUrl is null)
        {
            throw new ArgumentException($"{PublicUrlVariable} has to be an absolute http(s) origin without a trailing slash.", PublicUrlVariable);
        }

        return new(host!.Trim(), chosenPort, hasUsername ? username!.Trim() : null,
            hasPassword ? password : null, chosenSecurity, sender.Address,
            string.IsNullOrWhiteSpace(fromName) ? "hostingaffe" : fromName.Trim(), baseUrl);
    }
}
