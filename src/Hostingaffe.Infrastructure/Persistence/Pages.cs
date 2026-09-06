using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The page rows, ordered by the slug — the only order a flat wiki has.</summary>
public sealed class Pages(HostingaffeDbContext context) : IPages
{
    public Task<Page?> FindLiveAsync(Guid projectId, string slug, CancellationToken cancellationToken) =>
        context.Pages.SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Slug == slug && p.DeletedAt == null, cancellationToken);

    public Task<Page?> FindAnyAsync(Guid projectId, string slug, CancellationToken cancellationToken) =>
        context.Pages.SingleOrDefaultAsync(p => p.ProjectId == projectId && p.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Page>> FindLiveManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await context.Pages.Where(p => ids.Contains(p.Id) && p.DeletedAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Page>> ListAsync(Guid projectId, string? search, CancellationToken cancellationToken)
    {
        var rows = context.Pages.Where(p => p.ProjectId == projectId && p.DeletedAt == null);

        // The same `simple` configuration and the same words a search box
        // takes as everywhere else (docs/storage.md, Full-text search): a
        // filter, not a ranking, so the order stays the slug's.
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(p => EF.Property<NpgsqlTsVector>(p, "Search").Matches(EF.Functions.WebSearchToTsQuery("simple", search)));
        }

        return await rows.OrderBy(p => p.Slug).ToListAsync(cancellationToken);
    }

    public async Task<Page?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from page where id = {0} for update", [id], cancellationToken);
        return await context.Pages.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public void Add(Page page) => context.Pages.Add(page);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
