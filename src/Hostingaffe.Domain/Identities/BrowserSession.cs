using System.Security.Cryptography;

namespace Hostingaffe.Domain.Identities;

/// <summary>A server-side, individually revocable browser session.</summary>
public sealed class BrowserSession
{
    public static readonly TimeSpan IdleLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);
    private BrowserSession() { }
    private BrowserSession(Guid userId, byte[] hash, DateTimeOffset now) { Id = Guid.CreateVersion7(); UserId = userId; SecretHash = hash; CreatedAt = LastUsedAt = now; ExpiresAt = now.Add(AbsoluteLifetime); }
    public Guid Id { get; private init; }
    public Guid UserId { get; private init; }
    public byte[] SecretHash { get; private init; } = null!;
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset LastUsedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private init; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public static (BrowserSession Session, string Secret) Create(Guid userId, DateTimeOffset now) { var bytes = RandomNumberGenerator.GetBytes(32); return (new(userId, SHA256.HashData(bytes), now), Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_')); }
    public static byte[] Hash(string secret) => OneTimeSecret.Hash(secret);
    public bool IsValid(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now && LastUsedAt.Add(IdleLifetime) > now;
    public bool Touch(DateTimeOffset now) { if (!IsValid(now) || now - LastUsedAt < TouchInterval) return false; LastUsedAt = now; return true; }
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
