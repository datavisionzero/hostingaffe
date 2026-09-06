using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

public sealed record BrowserSessionSummary(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt, bool Current);

public sealed class SignInWithPassword(IIdentities identities, IPasswordHasher passwords,
    IBrowserSessions sessions, TimeProvider clock)
{
    private const string DummyHash = "$argon2id$v=19$m=65536,t=3,p=1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public async Task<(BrowserSession Session, string Secret)?> ExecuteAsync(string? email, string? password,
        CancellationToken cancellationToken)
    {
        string normalized;
        try { normalized = User.NormalizeEmailForComparison(email!); }
        catch (ArgumentException) { normalized = email?.Trim().ToLowerInvariant() ?? string.Empty; }
        var user = await identities.FindUserByNormalizedEmailAsync(normalized, cancellationToken);
        var usable = user is not null && user.State == UserState.Active && user.PasswordHash is not null;
        var candidate = password is { Length: >= 12 } ? password : (password ?? string.Empty).PadRight(12, '\0');
        var verified = await passwords.VerifyAsync(usable ? user!.PasswordHash! : DummyHash, candidate, cancellationToken);
        if (!usable || !verified) return null;
        var issued = BrowserSession.Create(user!.Id, clock.GetUtcNow());
        await sessions.AddAsync(issued.Session, cancellationToken);
        return issued;
    }
}

public sealed class ExchangeBootstrapToken(ITokens tokens, IIdentities identities, IPasswordHasher passwords,
    IBrowserSessions sessions, TimeProvider clock)
{
    public async Task<(BrowserSession Session, string Secret)?> ExecuteAsync(string? tokenSecret, string? password,
        CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        if (!TokenSecret.IsAcceptable(tokenSecret)) return null;
        var presented = await tokens.FindByHashAsync(TokenSecret.HashOf(tokenSecret!), cancellationToken);
        if (presented?.Identity is not User found || !found.Administrator || found.State != UserState.Active
            || presented.Token.Revoked) return null;
        var user = await identities.FindUserAsync(found.Id, cancellationToken);
        if (user is null || user.BootstrapExchangedAt is not null || user.PasswordHash is not null) return null;
        var now = clock.GetUtcNow();
        var passwordHash = await passwords.HashAsync(password!, cancellationToken);
        if (!await identities.TryRecordBootstrapExchangeAsync(user.Id, passwordHash, now, cancellationToken)) return null;
        var issued = BrowserSession.Create(user.Id, now);
        await sessions.AddAsync(issued.Session, cancellationToken);
        return issued;
    }

    internal static void ValidatePassword(string? password)
    {
        if (password is null || password.Length is < 12 or > 1024)
            throw Refusal.Validation("password", "A password is at least 12 and at most 1,024 characters.");
    }
}

public sealed class AcceptInvitation(IOneTimeSecrets secrets, IIdentities identities, IPasswordHasher passwords,
    IBrowserSessions sessions, TimeProvider clock)
{
    public async Task<(BrowserSession Session, string Secret)> ExecuteAsync(string? secret, string? password,
        CancellationToken cancellationToken)
    {
        ExchangeBootstrapToken.ValidatePassword(password);
        var record = await Consume(secret, OneTimeSecretPurpose.Invitation, secrets, clock.GetUtcNow(), cancellationToken);
        var user = await identities.FindUserAsync(record.UserId, cancellationToken)
            ?? throw new InvalidOperationException("An invitation has no user.");
        user.SetPassword(await passwords.HashAsync(password!, cancellationToken));
        user.Activate();
        await identities.RecordUserAsync(user, cancellationToken);
        var issued = BrowserSession.Create(user.Id, clock.GetUtcNow());
        await sessions.AddAsync(issued.Session, cancellationToken);
        return issued;
    }

    internal static async Task<OneTimeSecret> Consume(string? value, OneTimeSecretPurpose purpose,
        IOneTimeSecrets secrets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var found = await secrets.ConsumeAsync(OneTimeSecret.Hash(value!), purpose, now, cancellationToken);
            return found ?? throw new Refusal(RefusalCode.SecretExpired, "This link is expired or has already been used.");
        }
        catch (FormatException) { throw new Refusal(RefusalCode.SecretExpired, "This link is expired or has already been used."); }
        catch (ArgumentException) { throw new Refusal(RefusalCode.SecretExpired, "This link is expired or has already been used."); }
    }
}

public sealed class RequestPasswordRecovery(IIdentities identities, IOneTimeSecrets secrets, IEmailSender email,
    SmtpSettings smtp, TimeProvider clock)
{
    public async Task ExecuteAsync(string? address, CancellationToken cancellationToken)
    {
        if (!smtp.Configured) throw new Refusal(RefusalCode.SmtpNotConfigured, "Transactional email is not configured for this instance.");
        User? user = null;
        try { user = await identities.FindUserByNormalizedEmailAsync(User.NormalizeEmailForComparison(address!), cancellationToken); }
        catch (ArgumentException) { }
        if (user?.State != UserState.Active) return;
        var issued = OneTimeSecret.Issue(user.Id, OneTimeSecretPurpose.PasswordRecovery, clock.GetUtcNow());
        await secrets.AddReplacingLiveAsync(issued.Record, clock.GetUtcNow(), cancellationToken);
        var link = new Uri(smtp.PublicUrl!, $"/recover?secret={Uri.EscapeDataString(issued.Secret)}");
        await email.SendAsync(TransactionalEmailTemplates.PasswordRecovery(user.Email, user.Name, link), cancellationToken);
    }
}

public sealed class CompletePasswordRecovery(IOneTimeSecrets secrets, IIdentities identities, IPasswordHasher passwords,
    IBrowserSessions sessions, TimeProvider clock)
{
    public async Task ExecuteAsync(string? secret, string? password, CancellationToken cancellationToken)
    {
        ExchangeBootstrapToken.ValidatePassword(password);
        var now = clock.GetUtcNow();
        var record = await AcceptInvitation.Consume(secret, OneTimeSecretPurpose.PasswordRecovery, secrets, now, cancellationToken);
        var user = await identities.FindUserAsync(record.UserId, cancellationToken)
            ?? throw new InvalidOperationException("A recovery secret has no user.");
        user.SetPassword(await passwords.HashAsync(password!, cancellationToken));
        await identities.RecordUserAsync(user, cancellationToken);
        await sessions.RevokeAllAsync(user.Id, null, now, cancellationToken);
    }
}

public sealed class ListBrowserSessions(ICallerIdentity caller, IBrowserSessions sessions)
{
    public async Task<IReadOnlyList<BrowserSessionSummary>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var identity = caller.Caller.RequireUser("list browser sessions");
        return [.. (await sessions.ListAsync(identity.Id, cancellationToken)).Select(x =>
            new BrowserSessionSummary(x.Id, x.CreatedAt, x.LastUsedAt, x.ExpiresAt, x.Id == identity.SessionId))];
    }
}

public sealed class ChangePassword(ICallerIdentity caller, IIdentities identities, IPasswordHasher passwords,
    IBrowserSessions sessions, TimeProvider clock)
{
    public async Task ExecuteAsync(string? currentPassword, string? password, CancellationToken cancellationToken)
    {
        ExchangeBootstrapToken.ValidatePassword(password);
        var identity = caller.Caller.RequireUser("change a password");
        var user = await identities.FindUserAsync(identity.Id, cancellationToken) ?? throw new InvalidOperationException("The caller user is missing.");
        if (user.PasswordHash is null || !await passwords.VerifyAsync(user.PasswordHash, currentPassword ?? string.Empty, cancellationToken))
            throw new Refusal(RefusalCode.Unauthenticated, "The current password is not correct.");
        user.SetPassword(await passwords.HashAsync(password!, cancellationToken));
        await identities.RecordUserAsync(user, cancellationToken);
        await sessions.RevokeAllAsync(user.Id, identity.SessionId, clock.GetUtcNow(), cancellationToken);
    }
}
