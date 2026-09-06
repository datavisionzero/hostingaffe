namespace Hostingaffe.Domain.Identities;

public enum UserState { Invited, Active, Deactivated }

/// <summary>
/// A human identity, authenticated by a session in the browser or by a user
/// token at the console, and possibly holding the administrator role
/// (<c>CONTEXT.md</c>, User).
/// </summary>
public sealed class User : Identity
{
    private User()
    {
    }

    private User(Guid id, string name, string email, UserState state, bool administrator, DateTimeOffset createdAt)
        : base(id, name, administrator, createdAt)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
        State = state;
    }

    public override IdentityKind Kind => IdentityKind.User;

    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public UserState State { get; private set; }
    public string? PasswordHash { get; private set; }
    public DateTimeOffset? BootstrapExchangedAt { get; private set; }

    public static User Create(string name, string email, bool administrator, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), email, UserState.Active, administrator, createdAt);

    // Kept while the cut-two user-creation surface is replaced by invitations
    // in the next ticket. It never escapes as a sign-in address.
    public static User Create(string name, bool administrator, DateTimeOffset createdAt) =>
        Create(name, $"{Guid.CreateVersion7():N}@migration.invalid", administrator, createdAt);

    public static User Invite(string name, string email, bool administrator, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), NormalizeName(name), email, UserState.Invited, administrator, createdAt);

    public void SetPassword(string encodedHash) => PasswordHash =
        string.IsNullOrWhiteSpace(encodedHash) ? throw new ArgumentException("A password hash is required.", nameof(encodedHash)) : encodedHash;

    public void Activate() => State = UserState.Active;
    public void Deactivate() => State = UserState.Deactivated;
    public void Reactivate() => State = UserState.Active;
    public void ChangeAdministratorRole(bool administrator) => Administrator = administrator;
    public void ChangeEmail(string email)
    {
        Email = NormalizeEmail(email);
        NormalizedEmail = NormalizeEmailForComparison(email);
    }
    public void RecordBootstrapExchange(DateTimeOffset at) => BootstrapExchangedAt ??= at;

    public static string NormalizeEmail(string email)
    {
        var trimmed = email?.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100 || !System.Net.Mail.MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An email address is required and is at most 100 characters.", nameof(email));
        return trimmed;
    }

    public static string NormalizeEmailForComparison(string email) => NormalizeEmail(email).ToLowerInvariant();
}
