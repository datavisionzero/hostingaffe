using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>A user as <c>GET /users</c> lists them: an <see cref="IdentityRef"/> plus the role and the date.</summary>
public sealed record UserSummary(Guid Id, IdentityKind Kind, string Name, string Email, UserState State,
    bool Administrator, DateTimeOffset CreatedAt)
{
    public static UserSummary Of(User user) => new(user.Id, user.Kind, user.Name, user.Email, user.State,
        user.Administrator, user.CreatedAt);
}

/// <summary>A token as a list shows it: enough to tell it apart and to revoke it, nothing that admits.</summary>
public sealed record TokenSummary(Guid Id, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt)
{
    public static TokenSummary Of(Token token) => new(token.Id, token.Prefix, token.CreatedAt, token.RevokedAt);
}

/// <summary>An agent as <c>GET /agents</c> lists them: its owner and its one token beside it.</summary>
public sealed record AgentSummary(
    Guid Id,
    IdentityKind Kind,
    string Name,
    IdentityRef Owner,
    DateTimeOffset CreatedAt,
    TokenSummary Token,
    AgentMetadata? Metadata,
    DateTimeOffset? MetadataReportedAt);

/// <summary>
/// The secret, shown once (VISION 12): here and in no other response. What the
/// row keeps of it is the prefix and the hash.
/// </summary>
public sealed record IssuedToken(Guid Id, string Prefix, string Secret, DateTimeOffset CreatedAt);

/// <summary>What <c>POST /users</c> answers: the user and, once, their first token.</summary>
public sealed record CreatedUser(
    Guid Id,
    IdentityKind Kind,
    string Name,
    bool Administrator,
    DateTimeOffset CreatedAt,
    IssuedToken Token);

/// <summary>What <c>POST /agents</c> answers: the agent and, once, its token.</summary>
public sealed record CreatedAgent(
    Guid Id,
    IdentityKind Kind,
    string Name,
    IdentityRef Owner,
    DateTimeOffset CreatedAt,
    IssuedToken Token);
