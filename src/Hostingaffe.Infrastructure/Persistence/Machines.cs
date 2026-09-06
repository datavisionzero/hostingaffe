using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The machine rows, ordered by the key — the order an operator reads them in.</summary>
public sealed class Machines(HostingaffeDbContext context) : IMachines
{
    public Task<Machine?> FindAnyAsync(string key, CancellationToken cancellationToken) =>
        context.Machines.SingleOrDefaultAsync(m => m.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Machine>> ListAsync(
        Status? status, MachineKind? kind, bool retired, CancellationToken cancellationToken)
    {
        var rows = context.Machines.Where(m => m.DeletedAt == null);

        if (status is { } wanted)
        {
            rows = rows.Where(m => m.Status == wanted);
        }
        else if (!retired)
        {
            // Retiring is the normal end, and a default list is what is still
            // there. A retired machine stays reachable by its key.
            rows = rows.Where(m => m.Status != Status.Retired);
        }

        if (kind is { } sort)
        {
            rows = rows.Where(m => m.Kind == sort);
        }

        return await rows.OrderBy(m => m.Key).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Machine>> OnHostAsync(
        Guid hostId, DateTimeOffset? deletedAt, CancellationToken cancellationToken) =>
        await context.Machines
            .Where(m => m.HostId == hostId && (deletedAt == null ? m.DeletedAt == null : m.DeletedAt == deletedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Machines
                .Where(m => wanted.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Key, cancellationToken);
    }

    public async Task<Machine?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from machine where id = {0} for update", [id], cancellationToken);
        return await context.Machines.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    /// <summary>
    /// Walks the host chain upward from <paramref name="candidate"/>. A chain is
    /// as deep as a VM inside a VM inside a host, which is to say short; the
    /// walk is bounded by the rows it has already seen so that a chain already
    /// closed in the database cannot spin here.
    /// </summary>
    public async Task<bool> RunsOnAsync(Guid candidate, Guid machine, CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        var at = candidate;

        while (seen.Add(at))
        {
            var host = await context.Machines
                .Where(m => m.Id == at)
                .Select(m => m.HostId)
                .SingleOrDefaultAsync(cancellationToken);

            if (host is not { } next)
            {
                return false;
            }

            if (next == machine)
            {
                return true;
            }

            at = next;
        }

        return false;
    }

    public void Add(Machine machine) => context.Machines.Add(machine);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
