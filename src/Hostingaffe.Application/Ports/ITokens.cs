using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// A token row and the identity it belongs to, as authentication reads them:
/// one lookup, both rows, because every request pays for it.
/// </summary>
public sealed record PresentedToken(Token Token, Identity Identity);

/// <summary>
/// The token rows: found by the hash of the secret a request presents, and by
/// id or identity for the acts that list and revoke them.
/// </summary>
/// <remarks>
/// The authentication lookup is one statement on the unique index the hash
/// column carries and nothing else (<c>docs/storage.md</c>, Identities and
/// tokens). Revoked rows are returned too: telling a revoked token from an
/// unknown one is the act's business, and the answer it gives is the same for
/// both.
/// </remarks>
public interface ITokens
{
    Task<PresentedToken?> FindByHashAsync(byte[] secretHash, CancellationToken cancellationToken);

    Task<Token?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The one token of an agent — there is exactly one (<c>token_agent</c>).</summary>
    Task<Token?> FindAgentTokenAsync(Guid agentId, CancellationToken cancellationToken);

    /// <summary>A user's tokens, oldest first, revoked ones included.</summary>
    Task<IReadOnlyList<Token>> ListUserTokensAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A further token for an identity that already has one — a user's, always.</summary>
    Task AddAsync(Token token, CancellationToken cancellationToken);

    /// <summary>Writes back the revocation just made on <paramref name="token"/>.</summary>
    Task RecordRevocationAsync(Token token, CancellationToken cancellationToken);
}
