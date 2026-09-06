using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Ports;

public interface IProjectAccess
{
    Task<bool> HasAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> ProjectIdsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> UsersAsync(Guid projectId, CancellationToken cancellationToken);
    Task GrantAsync(ProjectAccess access, CancellationToken cancellationToken);
    Task RevokeAsync(Guid projectId, Guid userId, CancellationToken cancellationToken);
}
