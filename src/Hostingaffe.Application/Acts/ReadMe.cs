using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// How every identity appears inside another object (<c>docs/api.md</c>,
/// Shapes).
/// </summary>
public sealed record IdentityRef(Guid Id, IdentityKind Kind, string Name)
{
    public static IdentityRef Of(Identity identity) => new(identity.Id, identity.Kind, identity.Name);
}

/// <summary>The token a caller came in under, as <c>GET /me</c> shows it.</summary>
/// <remarks>
/// The id is here so that a holder can revoke the token it is holding without
/// having to recognise it in a list — which is what <c>ha logout</c> does with
/// the session <c>ha login</c> put in the keychain (ADR 0005). An id admits
/// nobody; the secret is the prefix's other 38 characters and is in no answer
/// but the one that issued it.
/// </remarks>
public sealed record TokenRef(Guid Id, string Prefix, DateTimeOffset CreatedAt);

/// <summary>
/// The caller, as <c>GET /me</c> answers: an <see cref="IdentityRef"/> plus
/// <c>administrator</c>, the <c>owner</c> of an agent, and the token.
/// </summary>
public sealed record Me(
    Guid Id,
    IdentityKind Kind,
    string Name,
    bool Administrator,
    string? Email,
    IdentityRef? Owner,
    TokenRef? Token,
    AgentMetadata? Metadata,
    DateTimeOffset? MetadataReportedAt);

/// <summary>
/// Who am I — the one read every client makes first, to learn which kind of
/// token it holds and under which name it will appear in the history.
/// </summary>
public sealed class ReadMe(ICallerIdentity callerIdentity, IIdentities identities)
{
    public async Task<Me> ExecuteAsync(CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;

        var owner = caller.OwnerId is { } ownerId
            ? await identities.FindAsync(ownerId, cancellationToken)
            : null;
        var agent = caller.IsAgent
            ? await identities.FindAgentAsync(caller.Id, cancellationToken)
            : null;
        var user = caller.IsAgent
            ? null
            : await identities.FindUserAsync(caller.Id, cancellationToken);

        return new Me(
            caller.Id,
            caller.Kind,
            caller.Name,
            caller.Administrator,
            user?.Email,
            owner is null ? null : IdentityRef.Of(owner),
            caller.SessionId is null ? new TokenRef(caller.TokenId, caller.TokenPrefix, caller.TokenCreatedAt) : null,
            agent?.Metadata,
            agent?.MetadataReportedAt);
    }
}
