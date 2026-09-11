using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
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
/// pretending otherwise. So do the dependencies, and they are ids — the keys
/// are resolved once for a whole list where a shape is assembled, never once
/// per row.
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
        else if (!filter.Retired)
        {
            rows = rows.Where(i => i.Status != Status.Retired);
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

    public async Task<IReadOnlyList<Installation>> OnMachineAsync(
        Guid machineId, DateTimeOffset? deletedAt, CancellationToken cancellationToken) =>
        await context.Installations
            .Where(i => i.MachineId == machineId && (deletedAt == null ? i.DeletedAt == null : i.DeletedAt == deletedAt))
            .ToListAsync(cancellationToken);

    public Task<int> CountOnSoftwareAsync(Guid softwareId, CancellationToken cancellationToken) =>
        context.Installations.CountAsync(i => i.SoftwareId == softwareId && i.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<Installation>> LiveByKeysAsync(
        IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var wanted = keys?.Distinct(StringComparer.Ordinal).ToArray() ?? [];

        return wanted.Length == 0
            ? []
            : await context.Installations
                .Where(i => wanted.Contains(i.Key) && i.DeletedAt == null)
                .ToListAsync(cancellationToken);
    }

    // The edge read from the other side, and the reason `installation_depends_on`
    // carries an index on its target column. A deleted dependent does not count:
    // `needed_by` says what would fall out, and what is deleted has fallen out
    // already.
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> DependentsAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        if (wanted.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<Guid>>();
        }

        var found = await context.Installations
            .Where(i => i.DeletedAt == null)
            .SelectMany(
                i => i.DependsOn.Where(d => wanted.Contains(d.DependsOnId)),
                (i, d) => new { Target = d.DependsOnId, Dependent = i.Id })
            .ToListAsync(cancellationToken);

        return found
            .GroupBy(one => one.Target)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)[.. group.Select(one => one.Dependent)]);
    }

    public Task<int> CountDependentsAsync(Guid id, CancellationToken cancellationToken) =>
        context.Installations
            .CountAsync(i => i.DeletedAt == null && i.DependsOn.Any(d => d.DependsOnId == id), cancellationToken);

    public async Task<IReadOnlyList<Installation>> FindManyAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? []
            : await context.Installations.Where(i => wanted.Contains(i.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Installations
                .Where(i => wanted.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Key, cancellationToken);
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
