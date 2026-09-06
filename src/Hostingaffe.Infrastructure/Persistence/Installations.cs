using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Installations;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The installation rows, ordered by the key — the order an operator reads them
/// in.
/// </summary>
/// <remarks>
/// Every filter is an equality on a column, which is what makes "every
/// production installation without a backup" one query rather than a scan
/// (VISION 7). The ports come with the row: they are part of what an
/// installation is, and a second round trip per row would be the cost of
/// pretending otherwise.
/// </remarks>
public sealed class Installations(HostingaffeDbContext context) : IInstallations
{
    public Task<Installation?> FindAnyAsync(string key, CancellationToken cancellationToken) =>
        context.Installations.SingleOrDefaultAsync(i => i.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Installation>> ListAsync(
        InstallationFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var rows = context.Installations.Where(i => i.DeletedAt == null);

        if (filter.MachineId is { } machine)
        {
            rows = rows.Where(i => i.MachineId == machine);
        }

        if (filter.SoftwareId is { } software)
        {
            rows = rows.Where(i => i.SoftwareId == software);
        }

        if (filter.Environment is { } environment)
        {
            rows = rows.Where(i => i.Environment == environment);
        }

        if (filter.Role is { } role)
        {
            rows = rows.Where(i => i.Role == role);
        }

        if (filter.Status is { } status)
        {
            rows = rows.Where(i => i.Status == status);
        }

        if (filter.Backup is { } backup)
        {
            rows = rows.Where(i => i.Backup == backup);
        }

        if (filter.Monitoring is { } monitoring)
        {
            rows = rows.Where(i => i.Monitoring == monitoring);
        }

        if (filter.Logging is { } logging)
        {
            rows = rows.Where(i => i.Logging == logging);
        }

        return await rows.OrderBy(i => i.Key).ToListAsync(cancellationToken);
    }

    public async Task<Installation?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from installation where id = {0} for update", [id], cancellationToken);
        return await context.Installations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public void Add(Installation installation) => context.Installations.Add(installation);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
