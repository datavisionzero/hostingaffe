using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The software rows, ordered by the key — the order an operator reads them in.
/// </summary>
/// <remarks>
/// Named for the rows rather than for a plural the word does not have
/// (<c>CONTEXT.md</c>, Software): the adapter beside this one is
/// <c>Machines</c>, and <c>Softwares</c> is the word this product does not use.
/// </remarks>
public sealed class SoftwareRows(HostingaffeDbContext context) : ISoftware
{
    public Task<Software?> FindAnyAsync(string key, CancellationToken cancellationToken) =>
        context.Software.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Software>> ListAsync(CancellationToken cancellationToken) =>
        await context.Software
            .Where(s => s.DeletedAt == null)
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? new Dictionary<Guid, string>()
            : await context.Software
                .Where(s => wanted.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Key, cancellationToken);
    }

    public async Task<Software?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from software where id = {0} for update", [id], cancellationToken);
        return await context.Software.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public void Add(Software software) => context.Software.Add(software);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
