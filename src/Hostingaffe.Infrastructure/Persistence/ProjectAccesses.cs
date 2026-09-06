using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Infrastructure.Persistence;

public sealed class ProjectAccesses(HostingaffeDbContext context) : IProjectAccess
{
    public Task<bool> HasAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) =>
        context.ProjectAccesses.AnyAsync(a => a.UserId == userId && a.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ProjectIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await context.ProjectAccesses.Where(a => a.UserId == userId).Select(a => a.ProjectId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlyList<User>> UsersAsync(Guid projectId, CancellationToken cancellationToken) =>
        await (from access in context.ProjectAccesses
               join user in context.Users on access.UserId equals user.Id
               where access.ProjectId == projectId
               orderby user.Name
               select user).ToListAsync(cancellationToken);

    public async Task GrantAsync(ProjectAccess access, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            insert into project_access (project_id, user_id, granted_by, granted_at)
            values ({access.ProjectId}, {access.UserId}, {access.GrantedBy}, {access.GrantedAt})
            on conflict (project_id, user_id) do nothing
            """, cancellationToken);
    }

    public async Task RevokeAsync(Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        await context.ProjectAccesses.Where(a => a.ProjectId == projectId && a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
