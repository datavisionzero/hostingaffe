using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The epic rows, with progress counted from the issues at read time (VISION 7).</summary>
public sealed class Epics(HostingaffeDbContext context) : IEpics
{
    public Task<Epic?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Epics.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Epic?> FindLiveAsync(Guid projectId, int number, CancellationToken cancellationToken) =>
        context.Epics.SingleOrDefaultAsync(e => e.ProjectId == projectId && e.Number == number && e.DeletedAt == null, cancellationToken);

    public Task<Epic?> FindAnyAsync(Guid projectId, int number, CancellationToken cancellationToken) =>
        context.Epics.SingleOrDefaultAsync(e => e.ProjectId == projectId && e.Number == number, cancellationToken);

    public async Task<IReadOnlyList<Epic>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToArray();
        return list.Length == 0 ? [] : await context.Epics.Where(e => list.Contains(e.Id)).ToListAsync(cancellationToken);
    }

    public async Task<EpicPageRows> ListAsync(EpicQuery query, EpicPosition? after, int limit, CancellationToken cancellationToken)
    {
        var rows = context.Epics.Where(e => e.DeletedAt == null);

        var allowedProjectIds = query.AllowedProjectIds.ToArray();
        rows = rows.Where(e => allowedProjectIds.Contains(e.ProjectId));

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(e => e.ProjectId == projectId);
        }

        if (query.Closed is { } closed)
        {
            rows = rows.Where(e => (e.Status == EpicStatus.Closed) == closed);
        }

        foreach (var name in query.LabelNames)
        {
            rows = rows.Where(e => context.EpicLabels.Any(el =>
                el.EpicId == e.Id && context.Labels.Any(l => l.Id == el.LabelId && l.Name == name && l.DeletedAt == null)));
        }

        var total = await rows.CountAsync(cancellationToken);

        rows = rows.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Number).ThenByDescending(e => e.Id);
        if (after is not null)
        {
            var at = after.CreatedAt;
            var number = after.Number;
            var id = after.Id;
            rows = rows.Where(e => e.CreatedAt < at || (e.CreatedAt == at && (e.Number < number || (e.Number == number && e.Id.CompareTo(id) < 0))));
        }

        var page = await rows.Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;
        return new EpicPageRows(hasMore ? page[..limit] : page, total, hasMore);
    }

    public async Task<Epic?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A row is loaded for writing inside a transaction, or the lock is worth nothing.");
        }

        await context.Database.ExecuteSqlRawAsync("select id from epic where id = {0} for update", [id], cancellationToken);
        return await context.Epics.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Progress>> ProgressAsync(IReadOnlyCollection<Guid> epicIds, CancellationToken cancellationToken)
    {
        var counted = await context.IssueReads
            .Where(i => i.EpicId != null && epicIds.Contains(i.EpicId.Value))
            .GroupBy(i => i.EpicId!.Value)
            .Select(g => new
            {
                g.Key,
                Total = g.Count(),
                Done = g.Count(i => i.Status == IssueStatus.Done),
                Canceled = g.Count(i => i.Status == IssueStatus.Canceled),
            })
            .ToListAsync(cancellationToken);

        return epicIds.ToDictionary(
            id => id,
            id => counted.SingleOrDefault(c => c.Key == id) is { } c
                ? new Progress(c.Total, c.Done + c.Canceled, c.Done, c.Canceled)
                : new Progress(0, 0, 0, 0));
    }

    // The table, not the view: a deleted issue still references the epic, and
    // restoring it would find the epic gone.
    public Task<int> ReferencingIssuesAsync(Guid epicId, CancellationToken cancellationToken) =>
        context.Issues.CountAsync(i => i.EpicId == epicId, cancellationToken);

    public async Task<IReadOnlyList<EpicLabelRow>> LabelsOfAsync(IReadOnlyCollection<Guid> epicIds, CancellationToken cancellationToken) =>
        await (
            from el in context.EpicLabels
            join l in context.Labels on el.LabelId equals l.Id
            where epicIds.Contains(el.EpicId) && l.DeletedAt == null
            select new EpicLabelRow(el.EpicId, l)).ToListAsync(cancellationToken);

    public void Add(Epic epic) => context.Epics.Add(epic);

    public void Attach(EpicLabel attachment) => context.EpicLabels.Add(attachment);

    public Task DetachAsync(Guid epicId, Guid labelId, CancellationToken cancellationToken) =>
        context.EpicLabels.Where(el => el.EpicId == epicId && el.LabelId == labelId).ExecuteDeleteAsync(cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
