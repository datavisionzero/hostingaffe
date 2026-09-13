using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The report rows. Everything here reads by <c>(machine_id, received_at
/// desc)</c>, which is the one index the table has beside its number, because
/// every question asked of a report is either "the latest" or "the latest few".
/// </summary>
public sealed class Reports(HostingaffeDbContext context) : IReports
{
    public async Task<IReadOnlyDictionary<Guid, Report>> LatestManyAsync(
        IEnumerable<Guid> machineIds, CancellationToken cancellationToken)
    {
        var wanted = machineIds?.Distinct().ToArray() ?? [];

        if (wanted.Length == 0)
        {
            return new Dictionary<Guid, Report>();
        }

        // One row per machine, chosen in the database: a machine list must not
        // cost one query per line (docs/api.md, last_seen).
        var latest = await context.Reports
            .Where(report => wanted.Contains(report.MachineId))
            .GroupBy(report => report.MachineId)
            .Select(group => group.OrderByDescending(report => report.ReceivedAt).First())
            .ToListAsync(cancellationToken);

        return latest.ToDictionary(report => report.MachineId);
    }

    public Task<Report?> LatestAsync(Guid machineId, CancellationToken cancellationToken) =>
        context.Reports
            .Where(report => report.MachineId == machineId)
            .OrderByDescending(report => report.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Report?> FindAsync(Guid machineId, int number, CancellationToken cancellationToken) =>
        context.Reports.SingleOrDefaultAsync(
            report => report.MachineId == machineId && report.Number == number, cancellationToken);

    public async Task<(IReadOnlyList<Report> Page, int Total)> ListAsync(
        Guid machineId, int skip, int take, CancellationToken cancellationToken)
    {
        var all = context.Reports.Where(report => report.MachineId == machineId);

        var total = await all.CountAsync(cancellationToken);

        var page = await all
            .OrderByDescending(report => report.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (page, total);
    }

    /// <summary>
    /// One past the highest number this machine has ever used, swept rows
    /// included: a number is an address, and an address is not handed out twice.
    /// The sweep takes rows from the old end, so the highest is the latest.
    /// </summary>
    public async Task<int> NextNumberAsync(Guid machineId, CancellationToken cancellationToken) =>
        await context.Reports
            .Where(report => report.MachineId == machineId)
            .MaxAsync(report => (int?)report.Number, cancellationToken) is { } highest
            ? highest + 1
            : 1;

    public void Add(Report report) => context.Reports.Add(report);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// The sweep, in SQL because it is a delete over rows nothing has loaded.
    /// The latest report of every machine is exempt however old it is: a machine
    /// that fell silent six weeks ago must keep the one thing worth knowing
    /// about it — when it last spoke, and how it was doing then.
    /// </summary>
    public Task<int> SweepAsync(DateTimeOffset olderThan, int batch, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync(
            """
            delete from machine_report where id in (
                select r.id from machine_report r
                 where r.received_at < {0}
                   and r.received_at < (
                        select max(latest.received_at) from machine_report latest
                         where latest.machine_id = r.machine_id)
                 limit {1})
            """,
            [olderThan, batch], cancellationToken);
}
