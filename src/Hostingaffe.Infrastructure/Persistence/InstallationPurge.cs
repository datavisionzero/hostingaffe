using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The deliberately explicit exception to the ordinary, key-preserving sweep.</summary>
public sealed class InstallationPurge(HostingaffeDbContext context) : IInstallationPurge
{
    public async Task<PurgeTarget?> LockAsync(string key, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An installation is locked inside a transaction.");
        }

        // The reservation is the lock shared by a row that still exists and
        // one the automatic sweep already removed. Two purge calls cannot both
        // release it, and a concurrent creation cannot take it before commit.
        await context.Database.ExecuteSqlRawAsync(
            "select key from assigned_key where kind = 'installation' and key = {0} for update", [key], cancellationToken);
        await context.Database.ExecuteSqlRawAsync("select id from installation where key = {0} for update", [key], cancellationToken);
        return await context.Installations.AsNoTracking()
            .Where(i => i.Key == key)
            .Select(i => new PurgeTarget(i.Id, i.MachineId, i.DeletedAt != null))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ReferencesAsync(Guid? id, string key, CancellationToken cancellationToken)
    {
        var references = new List<string>();

        if (id is { } installationId)
        {
            // A machine deletion took this installation with it. Purging it
            // would make restoring that machine bring back only part of what
            // the deletion took, so restore the machine first.
            var deletedOwner = await context.Installations
                .Where(i => i.Id == installationId)
                .Join(context.Machines.Where(m => m.DeletedAt != null), i => i.MachineId, m => m.Id, (i, m) => m.Key)
                .SingleOrDefaultAsync(cancellationToken);
            if (deletedOwner is not null) references.Add("machine " + deletedOwner + " (deleted)");

            references.AddRange(await context.Pages
                .Where(p => p.InstallationId == installationId)
                .Select(p => "page " + p.Slug + " (attached)")
                .ToListAsync(cancellationToken));

            references.AddRange(await context.Installations
                .Where(i => i.DependsOn.Any(d => d.DependsOnId == installationId))
                .Select(i => "installation " + i.Key + " (depends_on)")
                .ToListAsync(cancellationToken));
        }

        // The instance does not validate Markdown on write (ADR 0007). The
        // explicit purge pays for a complete read so it does not create a new
        // dangling link, including one in a deleted row that could be restored.
        foreach (var page in await context.Pages.Select(p => new { p.Slug, p.Body }).ToListAsync(cancellationToken))
        {
            if (ImportLinks.PointsToInstallation(page.Body, key)) references.Add("page " + page.Slug + " (link)");
        }

        foreach (var machine in await context.Machines.Select(m => new { m.Key, m.Description }).ToListAsync(cancellationToken))
        {
            if (ImportLinks.PointsToInstallation(machine.Description, key)) references.Add("machine " + machine.Key + " (link)");
        }

        foreach (var software in await context.Software.Select(s => new { s.Key, s.Description }).ToListAsync(cancellationToken))
        {
            if (ImportLinks.PointsToInstallation(software.Description, key)) references.Add("software " + software.Key + " (link)");
        }

        foreach (var installation in await context.Installations
            .Where(i => id == null || i.Id != id.Value)
            .Select(i => new { i.Key, i.Description })
            .ToListAsync(cancellationToken))
        {
            if (ImportLinks.PointsToInstallation(installation.Description, key)) references.Add("installation " + installation.Key + " (link)");
        }

        return references.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public async Task RemoveAsync(Guid? id, string key, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An installation is purged inside a transaction.");
        }

        if (id is { } installationId)
        {
            // A file's revisions and an installation's own ports, secrets and
            // dependencies cascade in the database. Deployments do not.
            await context.Database.ExecuteSqlRawAsync("delete from file where installation_id = {0}", [installationId], cancellationToken);
            await context.Database.ExecuteSqlRawAsync("delete from deployment where installation_id = {0}", [installationId], cancellationToken);
            await context.Database.ExecuteSqlRawAsync("delete from installation where id = {0}", [installationId], cancellationToken);
        }

        await context.Database.ExecuteSqlRawAsync(
            "delete from assigned_key where kind = 'installation' and key = {0}", [key], cancellationToken);
    }
}
