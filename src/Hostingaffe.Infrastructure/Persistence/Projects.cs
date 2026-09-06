using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The project rows and the two counters on them.</summary>
public sealed class Projects(HostingaffeDbContext context) : IProjects
{
    public Task<Project?> FindByKeyAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(p => p.Key == key, cancellationToken);

    public Task<Project?> FindByKeyForReadAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Key == key, cancellationToken);

    public Task<Project?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> KeyTakenAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.AnyAsync(p => p.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken) =>
        await context.Projects.Where(p => p.DeletedAt == null).OrderBy(p => p.Key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAllAsync(CancellationToken cancellationToken) =>
        await context.Projects.OrderBy(p => p.Key).ToListAsync(cancellationToken);

    public async Task AddAsync(Project project, IEnumerable<Label> labels, Release release, ProjectAccess creatorAccess,
        CancellationToken cancellationToken)
    {
        context.Projects.Add(project);
        context.Labels.AddRange(labels);
        context.Releases.Add(release);
        context.ProjectAccesses.Add(creatorAccess);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException collision) when (collision.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "project_key",
        })
        {
            throw Refusal.Validation("key", $"The key {project.Key} is taken — by a project, or by a deleted one waiting out its grace period.");
        }
    }

    public Task SaveAsync(Project project, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    // The one statement of docs/storage.md: the row lock serialises concurrent
    // creators, the increment takes `count` numbers at once, and a rollback of
    // the surrounding transaction rolls the counter back with it.
    public Task<int> AllocateIssueNumbersAsync(Guid projectId, int count, CancellationToken cancellationToken) =>
        AllocateAsync(
            """update project set last_issue_number = last_issue_number + {0} where id = {1} and deleted_at is null returning last_issue_number as "Value" """,
            projectId, count, cancellationToken);

    public Task<int> AllocateEpicNumbersAsync(Guid projectId, int count, CancellationToken cancellationToken) =>
        AllocateAsync(
            """update project set last_epic_number = last_epic_number + {0} where id = {1} and deleted_at is null returning last_epic_number as "Value" """,
            projectId, count, cancellationToken);

    // Two literal statements rather than one with the column interpolated, so
    // that nothing here ever composes SQL out of a string.
    private async Task<int> AllocateAsync(string statement, Guid projectId, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var last = await context.Database
            .SqlQueryRaw<int>(statement, count, projectId)
            .ToListAsync(cancellationToken);

        return last.Count == 1
            ? last[0] - count + 1
            : throw new Refusal(RefusalCode.NotFound, $"No live project {projectId} to draw a key from.");
    }
}
