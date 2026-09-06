using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Files;

using File = Hostingaffe.Domain.Files.File;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The file rows, ordered by the path — the order they are read in, and the one
/// a directory listing has.
/// </summary>
/// <remarks>
/// The revisions come with the row: what a file says now is the newest of them,
/// so a read without them is a file without a content. A file of an owner it
/// does not belong to is not found, which is why every query names both.
/// </remarks>
public sealed class Files(HostingaffeDbContext context) : IFiles
{
    public async Task<File?> FindAnyAsync(Anchor owner, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return await Owned(owner).SingleOrDefaultAsync(f => f.Path == path, cancellationToken);
    }

    public async Task<IReadOnlyList<File>> ListAsync(Anchor owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return await Owned(owner)
            .Where(f => f.DeletedAt == null)
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<File>> UnderAsync(
        AnchorKind kind, IEnumerable<Guid> ids, DateTimeOffset? deletedAt, CancellationToken cancellationToken)
    {
        var wanted = ids?.Distinct().ToArray() ?? [];

        if (wanted.Length == 0)
        {
            return [];
        }

        var rows = kind is AnchorKind.Machine
            ? context.Files.Where(f => f.MachineId != null && wanted.Contains(f.MachineId.Value))
            : context.Files.Where(f => f.InstallationId != null && wanted.Contains(f.InstallationId.Value));

        return await rows
            .Where(f => deletedAt == null ? f.DeletedAt == null : f.DeletedAt == deletedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<File?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from file where id = {0} for update", [id], cancellationToken);
        return await context.Files.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public void Add(File file) => context.Files.Add(file);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private IQueryable<File> Owned(Anchor owner) =>
        owner.Kind is AnchorKind.Machine
            ? context.Files.Where(f => f.MachineId == owner.Id)
            : context.Files.Where(f => f.InstallationId == owner.Id);
}
