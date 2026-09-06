using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The register of keys that have been given out, and nothing else.</summary>
public sealed class Keys(HostingaffeDbContext context) : IKeys
{
    public Task<bool> AssignedAsync(Keyed kind, string key, CancellationToken cancellationToken) =>
        context.AssignedKeys.AnyAsync(a => a.Kind == kind && a.Key == key, cancellationToken);

    public void Assign(Keyed kind, string key) => context.AssignedKeys.Add(AssignedKey.To(kind, key));
}
