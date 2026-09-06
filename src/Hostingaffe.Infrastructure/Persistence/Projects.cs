using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The project rows.</summary>
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

    public async Task AddAsync(Project project, ProjectAccess creatorAccess, CancellationToken cancellationToken)
    {
        context.Projects.Add(project);
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
}
