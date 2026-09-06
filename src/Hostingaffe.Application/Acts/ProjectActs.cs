using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// A project as <c>docs/api.md</c> shapes it. The suffix is dropped in the
/// contract. <paramref name="InstructionsPage"/> is the slug of the page the
/// project designates (<c>CONTEXT.md</c>, Instructions), or <c>null</c>; the
/// text itself is not here, because a project is read to be administered and
/// the instructions are read with a ticket.
/// </summary>
public sealed record ProjectShape(
    string Key,
    string Name,
    string? InstructionsPage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProjectShape Of(Project project, string? instructionsPage) =>
        new(project.Key, project.Name, instructionsPage, project.CreatedAt, project.UpdatedAt);
}

/// <summary>
/// The slug behind a project's instructions pointer. The column holds the
/// page's id (<c>docs/storage.md</c>, Pages), and every reader of a project
/// wants the address instead — so the lookup lives once, here, and a list of
/// projects pays one query for all of them.
/// </summary>
public static class ProjectInstructions
{
    public static async Task<string?> SlugAsync(this IPages pages, Project project, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        return (await pages.SlugsAsync([project], cancellationToken)).GetValueOrDefault(project.Id);
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> SlugsAsync(
        this IPages pages, IReadOnlyCollection<Project> projects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(projects);

        var wanted = projects.Select(p => p.InstructionsPageId).OfType<Guid>().Distinct().ToArray();
        if (wanted.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = (await pages.FindLiveManyAsync(wanted, cancellationToken)).ToDictionary(page => page.Id, page => page.Slug);

        return projects
            .Where(p => p.InstructionsPageId is { } id && found.ContainsKey(id))
            .ToDictionary(p => p.Id, p => found[p.InstructionsPageId!.Value]);
    }
}

public sealed record AdminProjectShape(string Key, string Name, DateTimeOffset CreatedAt, DateTimeOffset? DeletedAt);

public sealed class ListAdminProjects(ICallerIdentity callerIdentity, IProjects projects)
{
    public async Task<IReadOnlyList<AdminProjectShape>> ExecuteAsync(string? deleted, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("list all projects");
        var filter = deleted?.Trim().ToLowerInvariant() ?? "false";
        if (filter is not ("true" or "false" or "all"))
            throw Refusal.Validation("deleted", "Deleted must be true, false, or all.");
        return [.. (await projects.ListAllAsync(cancellationToken))
            .Where(project => filter == "all" || project.Deleted == (filter == "true"))
            .Select(project => new AdminProjectShape(project.Key, project.Name, project.CreatedAt, project.DeletedAt))];
    }
}

/// <summary>
/// The lookups every project act starts with: the live project by key, or the
/// refusal that says what is there instead.
/// </summary>
public static class ProjectLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<Project> LiveAsync(
        this IProjects projects, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var project = await projects.FindByKeyAsync(key.Trim().ToUpperInvariant(), cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No project {key}.");

        return project.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Project {project.Key} is deleted and can be restored until at least {project.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = project.DeletedAt.Value + settings.DeletionGrace })
            : project;
    }
}

/// <summary>
/// A user creates a project: the key that will prefix everything in it, typed
/// by a person and never changed (ADR 0015).
/// </summary>
public sealed class CreateProject(ICallerIdentity callerIdentity, IProjects projects, TimeProvider clock)
{
    public async Task<ProjectShape> ExecuteAsync(string? key, string? name, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("create a project");

        var normalizedKey = Validated.Field("key", () => ProjectKey.Normalize(key!));
        var normalizedName = Validated.Field("name", () => Project.NormalizeName(name!));

        if (await projects.KeyTakenAsync(normalizedKey, cancellationToken))
        {
            throw Refusal.Validation("key", $"The key {normalizedKey} is taken — by a project, or by a deleted one waiting out its grace period.");
        }

        var now = clock.GetUtcNow();
        var project = Project.Create(normalizedKey, normalizedName, caller.Id, now);

        await projects.AddAsync(project, ProjectAccess.Grant(project.Id, caller.Id, caller.Id, now), cancellationToken);

        // A project is born designating no page: there is none yet.
        return ProjectShape.Of(project, null);
    }
}

/// <summary>Every project the caller sees — in cut one, every live project.</summary>
public sealed class ListProjects(ICallerIdentity callerIdentity, IProjects projects, IPages pages, IProjectAccess access)
{
    public async Task<IReadOnlyList<ProjectShape>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var ids = await access.ProjectIdsAsync(callerIdentity.Caller.OwnerId ?? callerIdentity.Caller.Id, cancellationToken);
        var mine = (await projects.ListAsync(cancellationToken)).Where(project => ids.Contains(project.Id)).ToArray();
        var slugs = await pages.SlugsAsync(mine, cancellationToken);

        return [.. mine.Select(project => ProjectShape.Of(project, slugs.GetValueOrDefault(project.Id)))];
    }
}

public sealed class ReadProject(IProjects projects, IPages pages, ProjectScope scope, InstanceSettings settings)
{
    public async Task<ProjectShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var project = await projects.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        return ProjectShape.Of(project, await pages.SlugAsync(project, cancellationToken));
    }
}

/// <summary>
/// What a <c>PATCH</c> carries: only what is present changes.
/// <paramref name="InstructionsPageGiven"/> tells an absent field from one
/// present as <c>null</c>, which is how the designation is taken away.
/// </summary>
public sealed record ProjectChanges(
    string? Name, bool InstructionsPageGiven = false, string? InstructionsPage = null);

/// <summary>
/// A user changes the name or the page every agent is handed with its ticket;
/// the key is immutable. The designation is a user's to make and not an
/// agent's, like everything else here: an agent that could point the project at
/// a page would be writing its own instructions.
/// </summary>
public sealed class ChangeProject(
    ICallerIdentity callerIdentity, IProjects projects, IPages pages, ProjectScope scope, InstanceSettings settings, TimeProvider clock)
{
    public async Task<ProjectShape> ExecuteAsync(string key, ProjectChanges changes, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireUser("change a project");
        ArgumentNullException.ThrowIfNull(changes);

        var project = await projects.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var now = clock.GetUtcNow();

        if (changes.Name is not null)
        {
            Validated.Field("name", () => { project.Rename(changes.Name, now); return true; });
        }

        if (changes.InstructionsPageGiven)
        {
            project.Instruct(await DesignatedAsync(project, changes.InstructionsPage, cancellationToken), now);
        }

        await projects.SaveAsync(project, cancellationToken);

        return ProjectShape.Of(project, await pages.SlugAsync(project, cancellationToken));
    }

    /// <summary>
    /// The page the slug names, in this project and live. A slug that names
    /// nothing is <c>validation</c> on the field it arrived in rather than
    /// <c>not-found</c>: the address is in the body, and what is wrong is what
    /// was typed.
    /// </summary>
    private async Task<Guid?> DesignatedAsync(Project project, string? slug, CancellationToken cancellationToken)
    {
        var wanted = slug?.Trim();
        if (string.IsNullOrEmpty(wanted))
        {
            return null;
        }

        var normalized = Validated.Field("instructions_page", () => Slug.Normalize(wanted));
        var page = await pages.FindLiveAsync(project.Id, normalized, cancellationToken)
            ?? throw Refusal.Validation("instructions_page", $"No page {project.Key}/{normalized}.");

        return page.Id;
    }
}

/// <summary>
/// An administrator deletes a project with everything in it (ADR 0013). The API
/// asks for nothing to be typed; the CLI does.
/// </summary>
public sealed class DeleteProject(ICallerIdentity callerIdentity, IProjects projects, InstanceSettings settings, TimeProvider clock)
{
    public async Task ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireAdministrator("delete a project");

        var project = await projects.LiveAsync(key, settings, cancellationToken);
        project.Delete(caller.Id, clock.GetUtcNow());

        await projects.SaveAsync(project, cancellationToken);
    }
}

/// <summary>Back, with everything in it. A project that is not deleted is <c>transition</c>.</summary>
public sealed class RestoreProject(ICallerIdentity callerIdentity, IProjects projects, IPages pages)
{
    public async Task<ProjectShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireAdministrator("restore a project");

        var project = await projects.FindByKeyAsync(key.Trim().ToUpperInvariant(), cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No project {key}.");

        if (!project.Deleted)
        {
            throw new Refusal(RefusalCode.Transition, $"Project {project.Key} is not deleted.");
        }

        project.Restore();
        await projects.SaveAsync(project, cancellationToken);

        return ProjectShape.Of(project, await pages.SlugAsync(project, cancellationToken));
    }
}
