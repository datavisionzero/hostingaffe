using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// Who is calling, as every act wants to know it: the identity the presented
/// token belongs to, which kind it is, whether it administers the instance, and
/// the token it came in under.
/// </summary>
/// <remarks>
/// It is a value and not the row, so that an act can be asked about a caller
/// in a unit test without a database, and so that the adapter that
/// authenticated the request hands the same thing to every act in it. The
/// token is here because <c>GET /me</c> shows it and because the metadata back
/// channel of cut two writes to it.
/// </remarks>
/// <param name="OwnerId">The owning user of an agent; <c>null</c> for a user.</param>
public sealed record Caller(
    Guid Id,
    IdentityKind Kind,
    string Name,
    bool Administrator,
    Guid? OwnerId,
    Guid TokenId,
    string TokenPrefix,
    DateTimeOffset TokenCreatedAt,
    Guid? SessionId = null)
{
    public bool IsUser => Kind is IdentityKind.User;

    public bool IsAgent => Kind is IdentityKind.Agent;

    public static Caller Of(Identity identity, Token token) =>
        new(
            identity.Id,
            identity.Kind,
            identity.Name,
            identity.Administrator,
            (identity as Agent)?.OwnerId,
            token.Id,
            token.Prefix,
            token.CreatedAt);

    public static Caller Of(User user, BrowserSession session) =>
        new(user.Id, user.Kind, user.Name, user.Administrator, null,
            Guid.Empty, string.Empty, default, session.Id);
}

/// <summary>
/// The identity of the caller, as the port the acts ask for
/// (<c>docs/codebase.md</c>). The HTTP adapter answers it from the request it
/// authenticated; a test answers it with whoever the test says is calling.
/// </summary>
public interface ICallerIdentity
{
    /// <summary>
    /// The authenticated caller. Asking on a request that has none is a bug in
    /// the adapter — every endpoint but <c>GET /version</c> is behind the door —
    /// and throws rather than answering with nobody.
    /// </summary>
    Caller Caller { get; }
}
