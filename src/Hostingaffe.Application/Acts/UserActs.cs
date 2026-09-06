using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// An administrator creates an invited user and sends the one-time activation
/// link (<c>docs/api.md</c>, Users, agents and tokens).
/// </summary>
public sealed class CreateUser(ICallerIdentity callerIdentity, IIdentities identities, IOneTimeSecrets secrets,
    IEmailSender emailSender, SmtpSettings smtp, TimeProvider clock)
{
    public async Task<UserSummary> ExecuteAsync(string? name, string? email, bool administrator, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("create a user");

        var normalized = Validated.Field("name", () => Identity.NormalizeName(name!));
        if (await identities.NameTakenAsync(normalized, cancellationToken))
        {
            throw Refusal.Validation("name", $"The name {normalized} is taken; names are unique across users and agents, whatever the case.");
        }

        if (!smtp.Configured)
            throw new Refusal(RefusalCode.SmtpNotConfigured, "Transactional email is not configured for this instance.");

        var normalizedEmail = Validated.Field("email", () => User.NormalizeEmail(email!));
        if (await identities.FindUserByNormalizedEmailAsync(User.NormalizeEmailForComparison(normalizedEmail), cancellationToken) is not null)
            throw new Refusal(RefusalCode.EmailExists, "That email address already belongs to a user.");

        var now = clock.GetUtcNow();
        var user = User.Invite(normalized, normalizedEmail, administrator, now);
        await identities.AddUserAsync(user, cancellationToken);
        var invitation = OneTimeSecret.Issue(user.Id, OneTimeSecretPurpose.Invitation, now);
        await secrets.AddReplacingLiveAsync(invitation.Record, now, cancellationToken);
        var link = new Uri(smtp.PublicUrl!, $"/activate?secret={Uri.EscapeDataString(invitation.Secret)}");
        await emailSender.SendAsync(TransactionalEmailTemplates.Invitation(user.Email, user.Name, link), cancellationToken);
        return UserSummary.Of(user);
    }
}

/// <summary>Every user. The list is people; it is not paginated.</summary>
public sealed class ListUsers(ICallerIdentity callerIdentity, IIdentities identities)
{
    public async Task<IReadOnlyList<UserSummary>> ExecuteAsync(CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("list users");

        return [.. (await identities.ListUsersAsync(cancellationToken)).Select(UserSummary.Of)];
    }
}

/// <summary>A user changes the name shown on future history entries.</summary>
public sealed class RenameUser(ICallerIdentity callerIdentity, IIdentities identities)
{
    public async Task<UserSummary> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("change their name");
        var user = await identities.FindUserAsync(caller.Id, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, "No user has that id.");
        Validated.Field("name", () => { user.Rename(name!); return true; });
        await identities.RecordUserAsync(user, cancellationToken);
        return UserSummary.Of(user);
    }
}

public sealed class ResendInvitation(ICallerIdentity callerIdentity, IIdentities identities, IOneTimeSecrets secrets,
    IEmailSender emailSender, SmtpSettings smtp, TimeProvider clock)
{
    public async Task ExecuteAsync(string? address, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("resend an invitation");
        if (!smtp.Configured) throw new Refusal(RefusalCode.SmtpNotConfigured, "Transactional email is not configured for this instance.");
        var id = await IdentityAddress.ResolveAsync(address, identities, cancellationToken);
        var user = (id is null ? null : await identities.FindUserAsync(id.Value, cancellationToken))
            ?? throw new Refusal(RefusalCode.NotFound, $"No user {address}.");
        if (user.State != UserState.Invited) throw new Refusal(RefusalCode.Transition, "Only an invited user has an invitation to resend.");
        var issued = OneTimeSecret.Issue(user.Id, OneTimeSecretPurpose.Invitation, clock.GetUtcNow());
        await secrets.AddReplacingLiveAsync(issued.Record, clock.GetUtcNow(), cancellationToken);
        var link = new Uri(smtp.PublicUrl!, $"/activate?secret={Uri.EscapeDataString(issued.Secret)}");
        await emailSender.SendAsync(TransactionalEmailTemplates.Invitation(user.Email, user.Name, link), cancellationToken);
    }
}

public sealed class ChangeUserLifecycle(ICallerIdentity callerIdentity, IIdentities identities, TimeProvider clock)
{
    public async Task<UserSummary> ExecuteAsync(string? address, UserLifecycleChange change, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("change a user's lifecycle");
        var id = await IdentityAddress.ResolveAsync(address, identities, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No user {address}.");
        var outcome = await identities.ChangeLifecycleAsync(id, change, clock.GetUtcNow(), cancellationToken);
        if (outcome == UserLifecycleOutcome.NotFound) throw new Refusal(RefusalCode.NotFound, $"No user {address}.");
        if (outcome == UserLifecycleOutcome.LastAdministrator)
            throw new Refusal(RefusalCode.LastAdministrator, "Deactivation or demotion would leave no active administrator.");
        if (outcome == UserLifecycleOutcome.InvalidState)
            throw new Refusal(RefusalCode.Transition, "The user is already in the requested state.");
        return UserSummary.Of((await identities.FindUserAsync(id, cancellationToken))!);
    }
}

public sealed class RequestEmailChange(ICallerIdentity callerIdentity, IIdentities identities, IOneTimeSecrets secrets,
    IEmailSender emailSender, SmtpSettings smtp, TimeProvider clock)
{
    public async Task ExecuteAsync(string? email, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("change an email address");
        if (!smtp.Configured) throw new Refusal(RefusalCode.SmtpNotConfigured, "Transactional email is not configured for this instance.");
        var normalized = Validated.Field("email", () => User.NormalizeEmail(email!));
        if (await identities.FindUserByNormalizedEmailAsync(User.NormalizeEmailForComparison(normalized), cancellationToken) is not null)
            throw new Refusal(RefusalCode.EmailExists, "That email address already belongs to a user.");
        var user = await identities.FindUserAsync(caller.Id, cancellationToken) ?? throw new Refusal(RefusalCode.NotFound, "No user has that id.");
        var issued = OneTimeSecret.Issue(user.Id, OneTimeSecretPurpose.EmailChange, clock.GetUtcNow(), normalized);
        await secrets.AddReplacingLiveAsync(issued.Record, clock.GetUtcNow(), cancellationToken);
        var link = new Uri(smtp.PublicUrl!, $"/confirm-email?secret={Uri.EscapeDataString(issued.Secret)}");
        await emailSender.SendAsync(TransactionalEmailTemplates.EmailConfirmation(normalized, user.Name, link), cancellationToken);
    }
}

public sealed class ConfirmEmailChange(IOneTimeSecrets secrets, IIdentities identities, TimeProvider clock)
{
    public async Task ExecuteAsync(string? secret, CancellationToken cancellationToken)
    {
        byte[] hash;
        try { hash = OneTimeSecret.Hash(secret!); }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        { throw Refusal.Validation("secret", "A valid email-change secret is required."); }
        var record = await secrets.ConsumeAsync(hash, OneTimeSecretPurpose.EmailChange, clock.GetUtcNow(), cancellationToken)
            ?? throw new Refusal(RefusalCode.SecretExpired, "The email-change link is expired, replaced, or has already been used.");
        var user = await identities.FindUserAsync(record.UserId, cancellationToken)
            ?? throw new Refusal(RefusalCode.SecretExpired, "The email-change link is no longer usable.");
        user.ChangeEmail(record.PendingEmail!);
        await identities.RecordUserAsync(user, cancellationToken);
    }
}
