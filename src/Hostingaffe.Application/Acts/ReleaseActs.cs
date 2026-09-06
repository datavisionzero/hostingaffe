using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Application.Acts;

public sealed record ReleaseSummaryShape(string Name, ReleaseStatus Status, string Description, DateTimeOffset? PublishedAt, IdentityRef? PublishedBy, int Issues);
public sealed record ReleaseShape(string Name, ReleaseStatus Status, string Description, DateTimeOffset? PublishedAt, IdentityRef? PublishedBy, IReadOnlyList<IssueSummaryShape> Issues);
public sealed record PublishReleaseRequest(string? Name, string? Description);

/// <summary>
/// What a <c>PATCH</c> on a release carries. Both fields leave what they do not
/// name: an empty description clears the notes, and only the newest publication
/// takes a new name.
/// </summary>
public sealed record ChangeReleaseRequest(string? Name, string? Description);

public sealed class ReleaseAssembler(IReleases releases, IIdentities identities, IssueAssembler issues)
{
    public async Task<ReleaseSummaryShape> SummaryAsync(Release release, CancellationToken ct)
    {
        var rows = await releases.IssuesAsync(release.Id, ct);
        return new(DisplayName(release), release.Status, release.Description, release.PublishedAt, await PublisherAsync(release, ct), rows.Count);
    }

    public async Task<ReleaseShape> CompleteAsync(Release release, CancellationToken ct)
    {
        var rows = await releases.IssuesAsync(release.Id, ct);
        return new(DisplayName(release), release.Status, release.Description, release.PublishedAt, await PublisherAsync(release, ct), await issues.SummariesAsync(rows, ct));
    }

    private async Task<IdentityRef?> PublisherAsync(Release release, CancellationToken ct) =>
        release.PublishedBy is { } id && await identities.FindAsync(id, ct) is { } identity ? IdentityRef.Of(identity) : null;
    private static string DisplayName(Release release) => release.Name ?? "unreleased";
}

public sealed class ListReleases(IProjects projects, ProjectScope scope, IReleases releases, ReleaseAssembler assembler, InstanceSettings settings)
{
    public async Task<IReadOnlyList<ReleaseSummaryShape>> ExecuteAsync(string projectKey, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var result = new List<ReleaseSummaryShape>();
        foreach (var release in await releases.ListAsync(project.Id, ct)) result.Add(await assembler.SummaryAsync(release, ct));
        return result;
    }
}

public sealed class ReadRelease(IProjects projects, ProjectScope scope, IReleases releases, ReleaseAssembler assembler, InstanceSettings settings)
{
    public async Task<ReleaseShape> ExecuteAsync(string projectKey, string name, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var release = await releases.FindAsync(project.Id, name, ct) ?? throw new Refusal(RefusalCode.NotFound, $"No release {name} in {project.Key}.");
        return await assembler.CompleteAsync(release, ct);
    }
}

/// <summary>
/// Annotate the notes, and correct the name of the newest publication.
/// Renaming an older one is refused: the record of what shipped is not
/// rewritten, and a typo in the name of the release just cut is the one thing
/// that is a fumble rather than history (VISION 7).
/// </summary>
public sealed class ChangeRelease(IProjects projects, ProjectScope scope, IReleases releases, ITransactions transactions, ReleaseAssembler assembler, InstanceSettings settings, TimeProvider clock)
{
    public async Task<ReleaseShape> ExecuteAsync(string projectKey, string name, ChangeReleaseRequest request, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var renamed = request.Name is null ? null : Validated.Field("name", () => Release.NormalizeName(request.Name));
        var changed = await transactions.RunAsync(async () =>
        {
            var release = await releases.LoadForWriteAsync(project.Id, name, ct) ?? throw new Refusal(RefusalCode.NotFound, $"No release {name} in {project.Key}.");
            var now = clock.GetUtcNow();

            if (renamed is not null && !string.Equals(renamed, release.Name, StringComparison.Ordinal))
            {
                if (release.Status is not ReleaseStatus.Published)
                {
                    throw new Refusal(RefusalCode.Transition, "The open release is named when it is published.");
                }

                var latest = await releases.LatestPublishedAsync(project.Id, ct);
                if (latest is null || latest.Id != release.Id)
                {
                    throw new Refusal(RefusalCode.Transition, $"{release.Name} is not the newest release of {project.Key}; only the newest publication can be renamed.");
                }

                if (await releases.NameTakenAsync(project.Id, renamed, ct))
                {
                    throw new Refusal(RefusalCode.ReleaseExists, $"Release {renamed} already exists.");
                }

                release.Rename(renamed, now);
            }

            if (request.Description is not null)
            {
                release.Describe(request.Description, now);
            }

            await releases.SaveAsync(ct);
            return release;
        }, ct);
        return await assembler.CompleteAsync(changed, ct);
    }
}

/// <summary>
/// Take the newest publication back: the release is the open one again, and
/// the empty open release that the publication created goes with it. The
/// correction of a fumble, not the rewriting of a record — a publication that
/// another has followed, or that work has closed on top of, stays (VISION 7).
/// </summary>
public sealed class RetractRelease(IProjects projects, ProjectScope scope, IReleases releases, ITransactions transactions, ReleaseAssembler assembler, InstanceSettings settings, TimeProvider clock)
{
    public async Task<ReleaseShape> ExecuteAsync(string projectKey, string name, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var retracted = await transactions.RunAsync(async () =>
        {
            var release = await releases.LoadForWriteAsync(project.Id, name, ct) ?? throw new Refusal(RefusalCode.NotFound, $"No release {name} in {project.Key}.");
            if (release.Status is not ReleaseStatus.Published)
            {
                throw new Refusal(RefusalCode.Transition, "The open release was never published.");
            }

            var latest = await releases.LatestPublishedAsync(project.Id, ct);
            if (latest is null || latest.Id != release.Id)
            {
                throw new Refusal(RefusalCode.Transition, $"{release.Name} is not the newest release of {project.Key}; a publication another has followed stays.");
            }

            var open = await releases.LoadOpenForWriteAsync(project.Id, ct) ?? throw new InvalidOperationException("Project has no open release.");
            if (await releases.IssueCountAsync(open.Id, ct) > 0)
            {
                throw new Refusal(RefusalCode.Transition, $"Issues have closed into the open release since {release.Name} was published; take them out first or leave the publication standing.");
            }

            releases.Remove(open);
            release.Retract(clock.GetUtcNow());
            await releases.SaveAsync(ct);
            return release;
        }, ct);
        return await assembler.CompleteAsync(retracted, ct);
    }
}

/// <summary>
/// Put an issue into the open release by hand, or take it out of it — the
/// promise of VISION 7 that moving a ticket by hand still works, because a
/// ticket that has not shipped yet simply does not belong. A published release
/// is a record and does not take either act.
/// </summary>
public sealed class ChangeReleaseIssues(
    ICallerIdentity callerIdentity,
    IProjects projects,
    ProjectScope scope,
    IIssues issues,
    IReleases releases,
    IHistory history,
    ITransactions transactions,
    ReleaseAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public Task<ReleaseShape> AddAsync(string projectKey, string name, string issueKey, CancellationToken ct) =>
        WriteAsync(projectKey, name, issueKey, add: true, ct);

    public Task<ReleaseShape> RemoveAsync(string projectKey, string name, string issueKey, CancellationToken ct) =>
        WriteAsync(projectKey, name, issueKey, add: false, ct);

    private async Task<ReleaseShape> WriteAsync(string projectKey, string name, string issueKey, bool add, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var row = await issues.LiveAsync(issueKey, settings, ct);
        if (row.ProjectId != project.Id)
        {
            throw new Refusal(RefusalCode.NotFound, $"{row.Key} does not belong to {project.Key}.");
        }

        var changed = await transactions.RunAsync(async () =>
        {
            var release = await releases.LoadForWriteAsync(project.Id, name, ct) ?? throw new Refusal(RefusalCode.NotFound, $"No release {name} in {project.Key}.");
            if (release.Status is ReleaseStatus.Published)
            {
                throw new Refusal(RefusalCode.InPublishedRelease, $"{release.Name} is published and is a record; what it shipped stays as it is.");
            }

            if (add && await releases.InPublishedAsync(row.Id, ct))
            {
                throw new Refusal(RefusalCode.InPublishedRelease, $"{row.Key} shipped in a published release already.");
            }

            var wrote = add
                ? await releases.AttachAsync(release.Id, row.Id, ct)
                : await releases.DetachAsync(release.Id, row.Id, ct);

            if (wrote)
            {
                history.Add(HistoryEntry.OnIssue(
                    row.Id,
                    callerIdentity.Caller.Id,
                    clock.GetUtcNow(),
                    HistoryField.Release,
                    add ? null : "unreleased",
                    add ? "unreleased" : null));
            }

            await releases.SaveAsync(ct);
            return release;
        }, ct);
        return await assembler.CompleteAsync(changed, ct);
    }
}

public sealed class PublishRelease(ICallerIdentity callerIdentity, IProjects projects, ProjectScope scope, IReleases releases, ITransactions transactions, ReleaseAssembler assembler, InstanceSettings settings, TimeProvider clock)
{
    public async Task<ReleaseShape> ExecuteAsync(string projectKey, PublishReleaseRequest request, CancellationToken ct)
    {
        var project = await projects.LiveAsync(projectKey, settings, ct);
        await scope.RequireAsync(project.Id, ct);
        var name = Validated.Field("name", () => Release.NormalizeName(request.Name!));
        if (await releases.NameTakenAsync(project.Id, name, ct)) throw new Refusal(RefusalCode.ReleaseExists, $"Release {name} already exists.");
        var published = await transactions.RunAsync(async () =>
        {
            var open = await releases.LoadOpenForWriteAsync(project.Id, ct) ?? throw new InvalidOperationException("Project has no open release.");
            if (await releases.NameTakenAsync(project.Id, name, ct)) throw new Refusal(RefusalCode.ReleaseExists, $"Release {name} already exists.");
            var now = clock.GetUtcNow();
            open.Publish(name, request.Description, callerIdentity.Caller.Id, now);
            releases.Add(Release.Open(project.Id, now));
            await releases.SaveAsync(ct);
            return open;
        }, ct);
        return await assembler.CompleteAsync(published, ct);
    }
}
