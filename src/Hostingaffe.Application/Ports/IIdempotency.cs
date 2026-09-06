namespace Hostingaffe.Application.Ports;

/// <summary>What was answered to a write, kept for a replay (<c>docs/api.md</c>, Idempotency).</summary>
public sealed record StoredReply(byte[] RequestHash, short Status, string? Body, DateTimeOffset CreatedAt);

/// <summary>
/// The idempotency store: one reply per identity and key, for 24 hours. Keys
/// of different identities never meet, because the identity is half of the key.
/// </summary>
public interface IIdempotency
{
    Task<StoredReply?> FindAsync(Guid identityId, string key, CancellationToken cancellationToken);

    /// <summary>Keeps the reply; a second writer of the same identity and key loses quietly.</summary>
    Task StoreAsync(Guid identityId, string key, StoredReply reply, CancellationToken cancellationToken);
}
