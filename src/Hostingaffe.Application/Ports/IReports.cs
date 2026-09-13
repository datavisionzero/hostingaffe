using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The report rows (<c>docs/storage.md</c>, Reports). A report has no key: the
/// instance numbers it per machine, and the number is what addresses it.
/// </summary>
/// <remarks>
/// There is no update and no delete here. A report is written once, read, and
/// swept — nothing else ever happens to one (ADR 0015).
/// </remarks>
public interface IReports
{
    /// <summary>The latest report of each of these machines, by <c>received_at</c>; absent where there is none.</summary>
    Task<IReadOnlyDictionary<Guid, Report>> LatestManyAsync(
        IEnumerable<Guid> machineIds, CancellationToken cancellationToken);

    /// <summary>The latest report of one machine, or nothing where it has never reported.</summary>
    Task<Report?> LatestAsync(Guid machineId, CancellationToken cancellationToken);

    /// <summary>One by its number, or nothing.</summary>
    Task<Report?> FindAsync(Guid machineId, int number, CancellationToken cancellationToken);

    /// <summary>A page of a machine's reports, newest first, and how many there are in all.</summary>
    Task<(IReadOnlyList<Report> Page, int Total)> ListAsync(
        Guid machineId, int skip, int take, CancellationToken cancellationToken);

    /// <summary>The number the next report of this machine gets, counted from one.</summary>
    Task<int> NextNumberAsync(Guid machineId, CancellationToken cancellationToken);

    void Add(Report report);

    Task SaveAsync(CancellationToken cancellationToken);
}
