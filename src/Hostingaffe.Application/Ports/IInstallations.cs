using Hostingaffe.Domain;
using Hostingaffe.Domain.Installations;

// The model's word for whom an installation serves is `environment`
// (CONTEXT.md), and the type is named after it. `System` is implicitly
// imported everywhere, so the two are said apart here rather than by giving the
// domain a word it does not have.
using Environment = Hostingaffe.Domain.Installations.Environment;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The installation rows (<c>docs/storage.md</c>, Installations). An
/// installation is found by its key, because that is what an operator names it
/// by (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// The list takes every field that is a closed set and the two keys, because
/// the question VISION 7 names — "every production installation without a
/// backup" — is a filter over exactly those and nothing else. There is no
/// cursor, for the reason ADR 0012 keeps a list slim, and <c>depends_on</c> is
/// not among the filters: it is read whole where an installation or a machine
/// is read, and never as a query across the record (ADR 0014).
/// </remarks>
public interface IInstallations
{
    /// <summary>By key, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<Installation?> FindAnyAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every live installation the filters admit, by key. Every filter is optional.</summary>
    Task<IReadOnlyList<Installation>> ListAsync(
        InstallationFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// The installations of one machine: the live ones where
    /// <paramref name="deletedAt"/> is nothing, and otherwise the ones the
    /// machine took with it at exactly that moment — which is what a restore
    /// brings back and what a deletion of their own does not.
    /// </summary>
    Task<IReadOnlyList<Installation>> OnMachineAsync(
        Guid machineId, DateTimeOffset? deletedAt, CancellationToken cancellationToken);

    /// <summary>How many live installations still hang on a software — the number a refusal names.</summary>
    Task<int> CountOnSoftwareAsync(Guid softwareId, CancellationToken cancellationToken);

    /// <summary>
    /// The live rows for these keys, in one read: what a <c>depends_on</c> list
    /// arrives as, and what the act turns into the rows the Domain takes. A key
    /// nothing answers to is simply absent, and naming it is the act's.
    /// </summary>
    Task<IReadOnlyList<Installation>> LiveByKeysAsync(
        IEnumerable<string> keys, CancellationToken cancellationToken);

    /// <summary>
    /// Which live installations depend on each of these — the dependencies read
    /// from the other side, which is the direction a person asks in: what falls
    /// out if I touch this. An id nothing depends on is absent from the map
    /// rather than present with an empty list.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DependentsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>How many live installations depend on this one — the number a refusal names.</summary>
    Task<int> CountDependentsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The rows for these ids, deleted or not — for the document that has to say
    /// not only which installation a dependency names but which machine it lies
    /// on, because a key from another host is one the reader cannot look up.
    /// </summary>
    Task<IReadOnlyList<Installation>> FindManyAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The keys of the given rows, for the shapes that name one.</summary>
    Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Installation?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    void Add(Installation installation);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What a list is narrowed by: the two keys as rows, and one value of each
/// closed set. Nothing here is a search; each is an equality.
/// </summary>
public sealed record InstallationFilter
{
    /// <summary>
    /// Whether retired installations are in it. They are not, unless
    /// <see cref="Status"/> asks for them by name: retiring is the normal end,
    /// and a default list is what is still running (VISION 7).
    /// </summary>
    public bool Retired { get; init; }

    public Guid? MachineId { get; init; }

    public Guid? SoftwareId { get; init; }

    public Environment? Environment { get; init; }

    public Role? Role { get; init; }

    public Status? Status { get; init; }

    public Backup? Backup { get; init; }

    public Monitoring? Monitoring { get; init; }

    public Logging? Logging { get; init; }
}
