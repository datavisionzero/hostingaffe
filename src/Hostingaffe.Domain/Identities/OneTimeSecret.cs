using System.Security.Cryptography;

namespace Hostingaffe.Domain.Identities;

public enum OneTimeSecretPurpose { Invitation, PasswordRecovery, EmailChange }

/// <summary>A hashed, expiring and once-usable identity secret.</summary>
public sealed class OneTimeSecret
{
    private OneTimeSecret() { }
    private OneTimeSecret(Guid userId, OneTimeSecretPurpose purpose, byte[] hash, string? pendingEmail, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(); UserId = userId; Purpose = purpose; SecretHash = hash;
        PendingEmail = pendingEmail is null ? null : User.NormalizeEmail(pendingEmail);
        PendingNormalizedEmail = pendingEmail is null ? null : User.NormalizeEmailForComparison(pendingEmail);
        CreatedAt = now; ExpiresAt = now.Add(purpose == OneTimeSecretPurpose.Invitation ? TimeSpan.FromDays(7) : TimeSpan.FromHours(1));
    }
    public Guid Id { get; private init; }
    public Guid UserId { get; private init; }
    public OneTimeSecretPurpose Purpose { get; private init; }
    public byte[] SecretHash { get; private init; } = null!;
    public string? PendingEmail { get; private init; }
    public string? PendingNormalizedEmail { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset ExpiresAt { get; private init; }
    public DateTimeOffset? UsedAt { get; private set; }

    public static (OneTimeSecret Record, string Secret) Issue(Guid userId, OneTimeSecretPurpose purpose, DateTimeOffset now, string? pendingEmail = null)
    {
        if ((purpose == OneTimeSecretPurpose.EmailChange) != (pendingEmail is not null)) throw new ArgumentException("Only an email-change secret carries a pending email.");
        var bytes = RandomNumberGenerator.GetBytes(32);
        return (new(userId, purpose, SHA256.HashData(bytes), pendingEmail, now), Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }
    public static byte[] Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var padded = secret.Replace('-', '+').Replace('_', '/'); padded += new string('=', (4 - padded.Length % 4) % 4);
        return SHA256.HashData(Convert.FromBase64String(padded));
    }
    public bool IsLive(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;
    public void Consume(DateTimeOffset now) { if (!IsLive(now)) throw new InvalidOperationException("The secret is no longer live."); UsedAt = now; }
    public void Replace(DateTimeOffset now) { if (UsedAt is null) UsedAt = now; }
}
