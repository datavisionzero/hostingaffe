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

    public void Add(Provider provider) => context.Providers.Add(provider);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
