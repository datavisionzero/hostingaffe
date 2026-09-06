using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// An agent as <c>GET /agents</c> reads it: the row, its owner and its one token.
/// </summary>
public sealed record AgentRow(Agent Agent, Identity Owner, Token Token);

/// <summary>
/// The identity rows: users and agents, which are one table and one concept to
/// everything that points at them.
/// </summary>
public interface IIdentities
{
    /// <summary>Whether any identity exists at all — the bootstrap's one question.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    Task<Identity?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Agent?> FindAgentAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindUserAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>By name, regardless of case — how the API and the CLI address identities.</summary>
    Task<Identity?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Identity>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="name"/> — already normalized — is taken by any
    /// identity of either kind, regardless of case.
    /// </summary>
    Task<bool> NameTakenAsync(string name, CancellationToken cancellationToken);

    /// <summary>Every user, oldest first. The list is people; it is not paginated.</summary>
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken);

    /// <summary>Every agent with its owner and its token, oldest first, revoked ones included.</summary>
    Task<IReadOnlyList<AgentRow>> ListAgentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// An identity and its first token in one transaction: creating an agent
    /// creates both, and so does bootstrapping the first administrator and
    /// inviting a user (<c>docs/storage.md</c>). There is no way to add the one
    /// without the other, because an identity nothing can authenticate as is
    /// not an identity anybody asked for.
    /// </summary>
    /// <exception cref="Domain.Refusal">
    /// <c>validation</c> on <c>name</c> when the unique index refuses it — the
    /// race between the check above and the insert.
    /// </exception>
    Task AddAsync(Identity identity, Token token, CancellationToken cancellationToken);

    Task AddUserAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task RecordUserAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    Task<bool> TryRecordBootstrapExchangeAsync(Guid userId, string passwordHash, DateTimeOffset at,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>Writes back the name just given to <paramref name="agent"/>.</summary>
    /// <exception cref="Domain.Refusal">As <see cref="AddAsync"/>.</exception>
    Task RecordRenameAsync(Agent agent, CancellationToken cancellationToken);

    /// <summary>Writes the latest report and its immutable history row atomically.</summary>
    Task RecordMetadataAsync(Agent agent, AgentMetadataReport report, CancellationToken cancellationToken);

    Task<UserLifecycleOutcome> ChangeLifecycleAsync(Guid userId, UserLifecycleChange change,
        DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public enum UserLifecycleChange { Deactivate, Reactivate, GrantAdministrator, RevokeAdministrator }
public enum UserLifecycleOutcome { Changed, NotFound, InvalidState, LastAdministrator }
