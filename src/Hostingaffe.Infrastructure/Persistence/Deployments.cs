using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Deployments;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The deployment rows. They come back whole and unordered, because what the
/// order <em>means</em> is a rule of the model and lives in one place
/// (<c>Derived</c>) rather than in a query here.
/// </summary>
public sealed class Deployments(HostingaffeDbContext context) : IDeployments
{
    public async Task<IReadOnlyList<Deployment>> ListAsync(
        IEnumerable<Guid> installationIds, CancellationToken cancellationToken)
    {
        var wanted = installationIds?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? []
            : await context.Deployments
                .Where(d => wanted.Contains(d.InstallationId) && d.DeletedAt == null)
                .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Deployment>> UnderAsync(
        IEnumerable<Guid> installationIds, DateTimeOffset? deletedAt, CancellationToken cancellationToken)
    {
        var wanted = installationIds?.Distinct().ToArray() ?? [];

        return wanted.Length == 0
            ? []
            : await context.Deployments
                .Where(d => wanted.Contains(d.InstallationId)
                    && (deletedAt == null ? d.DeletedAt == null : d.DeletedAt == deletedAt))
                .ToListAsync(cancellationToken);
    }

    public Task<Deployment?> FindAnyAsync(Guid installationId, int number, CancellationToken cancellationToken) =>
        context.Deployments.SingleOrDefaultAsync(
            d => d.InstallationId == installationId && d.Number == number, cancellationToken);

    /// <summary>
    /// One past the highest number this installation has ever used, deleted rows
    /// included: a number is an address, and an address is not handed out twice.
    /// </summary>
    public async Task<int> NextNumberAsync(Guid installationId, CancellationToken cancellationToken) =>
        await context.Deployments
            .Where(d => d.InstallationId == installationId)
            .MaxAsync(d => (int?)d.Number, cancellationToken) is { } highest
            ? highest + 1
            : 1;

    public async Task<Deployment?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from deployment where id = {0} for update", [id], cancellationToken);
        return await context.Deployments.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public void Add(Deployment deployment) => context.Deployments.Add(deployment);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
