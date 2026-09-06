using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Infrastructure.Persistence;

public sealed class Releases(HostingaffeDbContext context) : IReleases
{
    public async Task<IReadOnlyList<Release>> ListAsync(Guid projectId, CancellationToken ct) =>
        await context.Releases.AsNoTracking().Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Status == ReleaseStatus.Open ? 0 : 1).ThenByDescending(r => r.PublishedAt).ToListAsync(ct);

    public Task<Release?> FindAsync(Guid projectId, string name, CancellationToken ct) =>
        name.Equals("unreleased", StringComparison.OrdinalIgnoreCase)
            ? context.Releases.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Status == ReleaseStatus.Open, ct)
            : context.Releases.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Name != null && r.Name.ToLower() == name.ToLower(), ct);

    public Task<Release?> LoadForWriteAsync(Guid projectId, string name, CancellationToken ct) =>
        name.Equals("unreleased", StringComparison.OrdinalIgnoreCase)
            ? context.Releases.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Status == ReleaseStatus.Open, ct)
            : context.Releases.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Name != null && r.Name.ToLower() == name.ToLower(), ct);

    public async Task<Release?> LoadOpenForWriteAsync(Guid projectId, CancellationToken ct) =>
        await context.Releases.FromSqlInterpolated($"select * from release where project_id = {projectId} and status = 'open' for update").SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<IssueRow>> IssuesAsync(Guid releaseId, CancellationToken ct) =>
        await (from ri in context.ReleaseIssues.AsNoTracking()
               join i in context.IssueReads on ri.IssueId equals i.Id
               join p in context.Projects on i.ProjectId equals p.Id
               where ri.ReleaseId == releaseId && p.DeletedAt == null
               orderby i.ParentId ?? i.Id, i.ParentId == null ? 0 : 1, i.Number
               select new IssueRow
               {
                   Id = i.Id,
                   ProjectId = i.ProjectId,
                   ProjectKey = p.Key,
                   Number = i.Number,
                   Title = i.Title,
                   Description = i.Description,
                   Result = i.Result,
                   Status = i.Status,
                   Ready = i.Ready,
                   Priority = i.Priority,
                   AssigneeId = i.AssigneeId,
                   EpicId = i.EpicId,
                   ParentId = i.ParentId,
                   ClaimedBy = i.ClaimedBy,
                   ClaimedAt = i.ClaimedAt,
                   ClaimExpiresAt = i.ClaimExpiresAt,
                   AuthorId = i.AuthorId,
                   CreatedAt = i.CreatedAt,
                   UpdatedAt = i.UpdatedAt,
                   ClosedAt = i.ClosedAt
               }).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> CurrentNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var rows = await (from ri in context.ReleaseIssues.AsNoTracking()
                          join r in context.Releases.AsNoTracking() on ri.ReleaseId equals r.Id
                          where ids.Contains(ri.IssueId)
                          orderby r.Status == ReleaseStatus.Open ? 0 : 1, r.PublishedAt descending
                          select new { ri.IssueId, Name = r.Name ?? "unreleased" }).ToListAsync(ct);
        return rows.GroupBy(x => x.IssueId).ToDictionary(g => g.Key, g => g.First().Name);
    }

    public Task<bool> NameTakenAsync(Guid projectId, string name, CancellationToken ct) =>
        context.Releases.AnyAsync(r => r.ProjectId == projectId && r.Name != null && r.Name.ToLower() == name.ToLower(), ct);

    public Task<Release?> LatestPublishedAsync(Guid projectId, CancellationToken ct) =>
        context.Releases.Where(r => r.ProjectId == projectId && r.Status == ReleaseStatus.Published)
            .OrderByDescending(r => r.PublishedAt).FirstOrDefaultAsync(ct);

    public Task<int> IssueCountAsync(Guid releaseId, CancellationToken ct) =>
        context.ReleaseIssues.CountAsync(x => x.ReleaseId == releaseId, ct);

    public async Task<bool> AttachAsync(Guid releaseId, Guid issueId, CancellationToken ct)
    {
        if (await context.ReleaseIssues.AnyAsync(x => x.ReleaseId == releaseId && x.IssueId == issueId, ct)) return false;
        context.ReleaseIssues.Add(ReleaseIssue.Attach(releaseId, issueId));
        return true;
    }

    public async Task<bool> DetachAsync(Guid releaseId, Guid issueId, CancellationToken ct) =>
        await context.ReleaseIssues.Where(x => x.ReleaseId == releaseId && x.IssueId == issueId).ExecuteDeleteAsync(ct) > 0;

    public void Remove(Release release) => context.Releases.Remove(release);

    public Task<bool> InPublishedAsync(Guid issueId, CancellationToken ct) =>
        (from ri in context.ReleaseIssues
         join r in context.Releases on ri.ReleaseId equals r.Id
         where ri.IssueId == issueId && r.Status == ReleaseStatus.Published
         select ri).AnyAsync(ct);

    public async Task AddDoneAsync(Issue issue, CancellationToken ct)
    {
        if (issue.Status != IssueStatus.Done) return;
        if (issue.ParentId is { } parentId)
        {
            var parent = await context.Issues.SingleAsync(i => i.Id == parentId, ct);
            if (parent.Status != IssueStatus.Done) return;
        }
        var open = await LoadOpenForWriteAsync(issue.ProjectId, ct) ?? throw new InvalidOperationException("Project has no open release.");
        var ids = issue.ParentId is null
            ? await context.Issues.Where(i => i.Id == issue.Id || i.ParentId == issue.Id && i.Status == IssueStatus.Done && i.DeletedAt == null).Select(i => i.Id).ToListAsync(ct)
            : new List<Guid> { issue.ParentId.Value, issue.Id };
        foreach (var id in ids.Distinct())
            if (!await context.ReleaseIssues.AnyAsync(x => x.ReleaseId == open.Id && x.IssueId == id, ct)) context.ReleaseIssues.Add(ReleaseIssue.Attach(open.Id, id));
    }

    public Task RemoveFromOpenAsync(Guid issueId, CancellationToken ct) =>
        (from ri in context.ReleaseIssues
         join r in context.Releases on ri.ReleaseId equals r.Id
         join i in context.Issues on ri.IssueId equals i.Id
         where (i.Id == issueId || i.ParentId == issueId) && r.Status == ReleaseStatus.Open
         select ri).ExecuteDeleteAsync(ct);

    public void Add(Release release) => context.Releases.Add(release);
    public Task SaveAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
