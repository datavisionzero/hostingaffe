using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>The one authorization decision used by every project-content act.</summary>
public sealed class ProjectScope(ICallerIdentity callerIdentity, IProjectAccess access)
{
    public Guid UserId => callerIdentity.Caller.OwnerId ?? callerIdentity.Caller.Id;

    public async Task RequireAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!await access.HasAsync(UserId, projectId, cancellationToken))
            throw new Refusal(RefusalCode.NotFound, "No such project or project content.");
    }

    public Task<IReadOnlySet<Guid>> ProjectIdsAsync(CancellationToken cancellationToken) =>
        access.ProjectIdsAsync(UserId, cancellationToken);
}

public sealed class ListProjectUsers(
    ICallerIdentity callerIdentity, IProjects projects, IProjectAccess access, InstanceSettings settings)
{
    public async Task<IReadOnlyList<UserSummary>> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("list project users");
        var project = await projects.LiveAsync(key, settings, cancellationToken);
        if (!caller.Administrator && !await access.HasAsync(caller.Id, project.Id, cancellationToken))
            throw new Refusal(RefusalCode.NotFound, $"No project {key}.");
        return [.. (await access.UsersAsync(project.Id, cancellationToken)).Select(UserSummary.Of)];
    }
}

public sealed class GrantProjectAccess(
    ICallerIdentity callerIdentity, IProjects projects, IProjectAccess access, IIdentities identities,
    InstanceSettings settings, TimeProvider clock)
{
    public async Task ExecuteAsync(string key, Guid userId, CancellationToken cancellationToken)
    {
        var administrator = callerIdentity.Caller.RequireAdministrator("grant project access");
        var project = await projects.LiveAsync(key, settings, cancellationToken);
        var user = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No user {userId}.");
        await access.GrantAsync(ProjectAccess.Grant(project.Id, user.Id, administrator.Id, clock.GetUtcNow()), cancellationToken);
    }
}

public sealed class RevokeProjectAccess(
    ICallerIdentity callerIdentity, IProjects projects, IProjectAccess access, IIdentities identities,
    InstanceSettings settings)
{
    public async Task ExecuteAsync(string key, Guid userId, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("revoke project access");
        var project = await projects.LiveAsync(key, settings, cancellationToken);
        _ = await identities.FindUserAsync(userId, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No user {userId}.");
        await access.RevokeAsync(project.Id, userId, cancellationToken);
    }
}
