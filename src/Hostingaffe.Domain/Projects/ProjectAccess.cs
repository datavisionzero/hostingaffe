namespace Hostingaffe.Domain.Projects;

/// <summary>
/// The assignment of a project to a user. Agents inherit their owner's
/// assignments and therefore never have rows of their own (CONTEXT.md).
/// </summary>
public sealed class ProjectAccess
{
    private ProjectAccess() { }

    private ProjectAccess(Guid projectId, Guid userId, Guid grantedBy, DateTimeOffset grantedAt)
    {
        ProjectId = projectId;
        UserId = userId;
        GrantedBy = grantedBy;
        GrantedAt = grantedAt;
    }

    public Guid ProjectId { get; private init; }
    public Guid UserId { get; private init; }
    public Guid GrantedBy { get; private init; }
    public DateTimeOffset GrantedAt { get; private init; }

    public static ProjectAccess Grant(Guid projectId, Guid userId, Guid grantedBy, DateTimeOffset grantedAt) =>
        new(projectId, userId, grantedBy, grantedAt);
}
