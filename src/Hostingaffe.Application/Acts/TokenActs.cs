using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>A user's own tokens, revoked ones included.</summary>
public sealed class ListTokens(ICallerIdentity callerIdentity, ITokens tokens)
{
    public async Task<IReadOnlyList<TokenSummary>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("list tokens");

        return [.. (await tokens.ListUserTokensAsync(caller.Id, cancellationToken)).Select(TokenSummary.Of)];
    }
}

/// <summary>
/// A further key for the caller, shown once. A user has as many as they create;
/// an agent has exactly one and is told <c>forbidden</c> — an agent that can
/// issue itself a second token has escaped its own identity (VISION 12).
/// </summary>
public sealed class CreateToken(ICallerIdentity callerIdentity, IIdentities identities, ITokens tokens, TimeProvider clock)
{
    public async Task<IssuedToken> ExecuteAsync(CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("create a token");

        var user = await identities.FindAsync(caller.Id, cancellationToken)
            ?? throw new InvalidOperationException($"The caller {caller.Id} has no row.");

        var secret = TokenSecret.Generate();
        var token = Token.Issue(user, secret, clock.GetUtcNow());

        await tokens.AddAsync(token, cancellationToken);

        return new IssuedToken(token.Id, token.Prefix, secret, token.CreatedAt);
    }
}

/// <summary>
/// Revoke one of the caller's own tokens. Another identity's token is
/// <c>not-found</c>: the id names nothing the caller can see.
/// </summary>
public sealed class RevokeToken(ICallerIdentity callerIdentity, ITokens tokens, TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("revoke a token");

        var token = await tokens.FindAsync(id, cancellationToken);
        if (token is null || token.IdentityId != caller.Id)
        {
            throw new Refusal(RefusalCode.NotFound, $"No token {id} of yours.");
        }

        if (token.Revoked)
        {
            return;
        }

        token.Revoke(clock.GetUtcNow());
        await tokens.RecordRevocationAsync(token, cancellationToken);
    }
}
