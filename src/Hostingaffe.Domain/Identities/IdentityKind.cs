namespace Hostingaffe.Domain.Identities;

/// <summary>
/// The two kinds an identity comes in — and, because a token is an agent or a
/// user's key and nothing else (ADR 0015), the two kinds a token comes in.
/// </summary>
public enum IdentityKind
{
    User,
    Agent,
}
