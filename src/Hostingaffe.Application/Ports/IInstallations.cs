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
/// cursor, for the reason ADR 0012 keeps a list slim.
/// </remarks>
public interface IInstallations
{
    /// <summary>By key, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<Installation?> FindAnyAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every live installation the filters admit, by key. Every filter is optional.</summary>
    Task<IReadOnlyList<Installation>> ListAsync(
        InstallationFilter filter, CancellationToken cancellationToken);

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
    public Guid? MachineId { get; init; }

    public Guid? SoftwareId { get; init; }

    public Environment? Environment { get; init; }

    public Role? Role { get; init; }

    public Status? Status { get; init; }

    public Backup? Backup { get; init; }

    public Monitoring? Monitoring { get; init; }

    public Logging? Logging { get; init; }
}
