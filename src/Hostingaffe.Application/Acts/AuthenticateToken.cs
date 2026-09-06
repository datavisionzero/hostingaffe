using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What a presented token admits: the caller it belongs to, or nobody.
/// </summary>
/// <remarks>
/// <para>
/// The first thing every endpoint but <c>GET /version</c> does, and the only
/// place any of them learns who is calling. The path is the one
/// <c>docs/storage.md</c> describes: read the secret out of the header, hash
/// it, find the row by the hash, and read the kind off the row — the server
/// tells a user token from an agent token, the client never says which it holds
/// (ADR 0015).
/// </para>
/// <para>
/// Nothing here tells a token that never existed apart from one that was
/// revoked, and nothing it returns says which it was: both are
/// <c>unauthenticated</c> (<c>docs/api.md</c>, Errors). A secret is not
/// compared, only looked up, so there is no timing to equalise — a miss and a
/// hit cost one index lookup each.
/// </para>
/// </remarks>
public sealed class AuthenticateToken(ITokens tokens, IIdentities identities)
{
    private const string Scheme = "Bearer";

    /// <summary>
    /// The caller behind <paramref name="authorization"/> — the value of the
    /// <c>Authorization</c> header — or <c>null</c> when it admits nobody.
    /// </summary>
    public async Task<Caller?> ExecuteAsync(string? authorization, CancellationToken cancellationToken)
    {
        if (!TryReadSecret(authorization, out var secret))
        {
            return null;
        }

        var presented = await tokens.FindByHashAsync(TokenSecret.HashOf(secret), cancellationToken);

        var disabled = presented?.Identity is User user && user.State != UserState.Active;
        if (presented?.Identity is Agent agent)
        {
            var owner = await identities.FindUserAsync(agent.OwnerId, cancellationToken);
            disabled = owner?.State != UserState.Active;
        }
        return presented is null || presented.Token.Revoked || disabled
            ? null
            : Caller.Of(presented.Identity, presented.Token);
    }

    /// <summary>
    /// Reads the secret out of a <c>Bearer</c> header value, and refuses there
    /// and then anything that is not one — the wrong scheme, nothing after it,
    /// or a length no secret has. None of that reaches the database.
    /// </summary>
    private static bool TryReadSecret(string? authorization, out string secret)
    {
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        var parts = authorization.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].Equals(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TokenSecret.IsAcceptable(parts[1]))
        {
            return false;
        }

        secret = parts[1];
        return true;
    }
}
