using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Providers;
using Microsoft.EntityFrameworkCore;

namespace Hostingaffe.Infrastructure.Persistence;

public sealed class Providers(HostingaffeDbContext context) : IProviders
{
    public Task<Provider?> FindAnyAsync(string key, CancellationToken cancellationToken) =>
        context.Providers.SingleOrDefaultAsync(provider => provider.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Provider>> ListAsync(CancellationToken cancellationToken) =>
        await context.Providers.Where(provider => provider.DeletedAt == null)
            .OrderBy(provider => provider.Key).ToListAsync(cancellationToken);

    // Include deleted machines: restoring one must not reveal a stranded key.
    public Task<bool> InUseAsync(string key, CancellationToken cancellationToken) =>
        context.Machines.AnyAsync(machine => machine.Provider == key, cancellationToken);

    public async Task<Provider?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A provider is loaded for writing inside a transaction.");
        }

        await context.Database.ExecuteSqlRawAsync(
            "select id from provider where id = {0} for update", [id], cancellationToken);
        var row = await context.Providers.SingleOrDefaultAsync(provider => provider.Id == id, cancellationToken);
        if (row is not null) await context.Entry(row).ReloadAsync(cancellationToken);
        return row;
    }

    public async Task<bool> AssignableForWriteAsync(string key, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A provider assignment is checked inside a transaction.");

        await context.Database.ExecuteSqlRawAsync(
            "select id from provider where key = {0} for update", [key], cancellationToken);
        return await context.Providers.AsNoTracking()
            .AnyAsync(provider => provider.Key == key && provider.DeletedAt == null, cancellationToken);
    }

    public void Add(Provider provider) => context.Providers.Add(provider);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
