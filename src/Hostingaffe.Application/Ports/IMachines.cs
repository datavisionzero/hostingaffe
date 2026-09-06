using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The machine rows (<c>docs/storage.md</c>, Machines). A machine is found by
/// its key, because that is what an operator names it by
/// (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// There is no cursor here yet. An instance holds one team's infrastructure,
/// and the number of machines a team rents is a number a person can read in one
/// screen; the list stays slim rather than paginated, for the reason ADR 0012
/// keeps a list slim.
/// </remarks>
public interface IMachines
{
    /// <summary>By key, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<Machine?> FindAnyAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Every live machine, by key. <paramref name="status"/> filters to one
    /// status and <paramref name="kind"/> to one kind; <paramref name="retired"/>
    /// says whether the retired ones are in it at all, and they are not unless
    /// <paramref name="status"/> asks for them by name.
    /// </summary>
    Task<IReadOnlyList<Machine>> ListAsync(
        Status? status, MachineKind? kind, bool retired, CancellationToken cancellationToken);

    /// <summary>The machines that run on this one, live or taken with it at exactly that moment.</summary>
    Task<IReadOnlyList<Machine>> OnHostAsync(
        Guid hostId, DateTimeOffset? deletedAt, CancellationToken cancellationToken);

    /// <summary>The keys of the given rows, for the shapes that name a host.</summary>
    Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Machine?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="machine"/> or
    /// runs on it, directly or through other hosts — what a host that would
    /// close a chain looks like from the outside.
    /// </summary>
    Task<bool> RunsOnAsync(Guid candidate, Guid machine, CancellationToken cancellationToken);

    void Add(Machine machine);

    Task SaveAsync(CancellationToken cancellationToken);
}
