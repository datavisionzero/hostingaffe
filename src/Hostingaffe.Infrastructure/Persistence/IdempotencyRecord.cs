namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// A stored answer to a write, replayed for 24 hours when the same identity
/// sends the same <c>Idempotency-Key</c> again (<c>docs/api.md</c>).
/// </summary>
/// <remarks>
/// The key is scoped to the identity, so two agents choosing the same key cannot
/// answer each other's requests; <see cref="RequestHash"/> is what tells a
/// replay from a reuse of the key for a different request, which is refused
/// (<c>docs/storage.md</c>, Idempotency). Rows older than a day go with the
/// purge at the end of any write transaction.
/// </remarks>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        // EF Core materializes through this; every other route goes through Of.
    }

    private IdempotencyRecord(
        Guid identityId,
        string key,
        byte[] requestHash,
        short status,
        string? body,
        DateTimeOffset createdAt)
    {
        IdentityId = identityId;
        Key = key;
        RequestHash = requestHash;
        Status = status;
        Body = body;
        CreatedAt = createdAt;
    }

    public Guid IdentityId { get; private init; }

    public string Key { get; private init; } = null!;

    /// <summary>SHA-256 of method, path and body.</summary>
    public byte[] RequestHash { get; private init; } = null!;

    /// <summary>The HTTP status the first answer carried.</summary>
    public short Status { get; private init; }

    /// <summary>The first answer's body, as JSON, or nothing.</summary>
    public string? Body { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public static IdempotencyRecord Of(
        Guid identityId,
        string key,
        byte[] requestHash,
        short status,
        string? body,
        DateTimeOffset createdAt) =>
        new(identityId, key, requestHash, status, body, createdAt);
}
