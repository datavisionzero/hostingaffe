namespace Hostingaffe.Domain.Identities;

/// <summary>
/// What authenticates a request: an agent token or a user token
/// (<c>CONTEXT.md</c>), told apart by the server from the row it already holds.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Kind"/> is copied from the identity so that authentication needs
/// no join — that is the one thing ADR 0015 asks the server to do on every
/// request. An agent has exactly one token and a user as many as they create;
/// the partial unique index on the table holds the former, and there is no act
/// that adds a token to an agent.
/// </para>
/// <para>
/// The secret is shown once and stored as its SHA-256; <see cref="Prefix"/> is
/// its first eight characters, shown in lists so that tokens can be told apart
/// without any of them being recoverable (VISION 12). Revoked is a timestamp,
/// not a deletion: a revoked token still names its identity everywhere it ever
/// acted and simply fails authentication (ADR 0013).
/// </para>
/// </remarks>
public sealed class Token
{
    public const int PrefixLength = 8;

    /// <summary>The length of a SHA-256 digest.</summary>
    public const int SecretHashLength = 32;

    private Token()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private Token(
        Guid id,
        Guid identityId,
        IdentityKind kind,
        string prefix,
        byte[] secretHash,
        DateTimeOffset createdAt)
    {
        Id = id;
        IdentityId = identityId;
        Kind = kind;
        Prefix = prefix;
        SecretHash = secretHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid IdentityId { get; private init; }

    public IdentityKind Kind { get; private init; }

    public string Prefix { get; private init; } = null!;

    public byte[] SecretHash { get; private init; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool Revoked => RevokedAt is not null;

    /// <summary>
    /// A token for <paramref name="identity"/>, of the kind the identity is.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="prefix"/> is not eight characters, or
    /// <paramref name="secretHash"/> is not a SHA-256 digest.
    /// </exception>
    public static Token Issue(Identity identity, string prefix, byte[] secretHash, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(secretHash);

        if (prefix?.Length != PrefixLength)
        {
            throw new ArgumentException(
                $"A token prefix is the first {PrefixLength} characters of the secret.", nameof(prefix));
        }

        if (secretHash.Length != SecretHashLength)
        {
            throw new ArgumentException("A secret hash is a SHA-256 digest.", nameof(secretHash));
        }

        return new Token(
            Guid.CreateVersion7(),
            identity.Id,
            identity.Kind,
            prefix,
            secretHash,
            createdAt);
    }

    /// <summary>
    /// A token for <paramref name="identity"/> from the secret itself: the
    /// prefix and the hash are derived here, and the secret is kept nowhere.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="secret"/> is shorter than
    /// <see cref="TokenSecret.MinimumLength"/> or longer than
    /// <see cref="TokenSecret.MaximumLength"/>.
    /// </exception>
    public static Token Issue(Identity identity, string secret, DateTimeOffset createdAt) =>
        Issue(identity, TokenSecret.PrefixOf(secret), TokenSecret.HashOf(secret), createdAt);

    /// <summary>
    /// Takes effect immediately and cannot be undone; revoking a revoked token
    /// changes nothing.
    /// </summary>
    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
