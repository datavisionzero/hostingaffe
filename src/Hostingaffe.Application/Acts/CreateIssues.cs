using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>One issue of a bulk create (<c>docs/api.md</c>, Creating several issues in one act).</summary>
/// <param name="Ref">A handle valid inside this request only; <c>blocked_by</c> and <c>blocks</c> take refs and keys alike.</param>
/// <param name="Status">Only <c>backlog</c> means anything: it parks the issue from birth.</param>
public sealed record NewIssue(
    string? Ref,
    string? Title,
    string? Description,
    Priority? Priority,
    bool? Ready,
    IReadOnlyList<string>? Labels,
    string? Epic,
    string? Parent,
    string? Assignee,
    IReadOnlyList<string>? BlockedBy,
    IReadOnlyList<string>? Blocks,
    IssueStatus? Status);

public sealed record CreateIssuesRequest(string? Project, IReadOnlyList<NewIssue>? Issues);

/// <summary>
/// The most important moment (VISION 10): several wired-up issues in one
/// transaction — the keys in one increment, the rows, then the edges, refusing a
/// cycle among them — and the whole request or none of it, because blockers
/// pointing at issues that do not exist break <c>next</c>.
/// </summary>
public sealed class CreateIssues(
    ICallerIdentity callerIdentity,
    ProjectScope scope,
    IProjects projects,
    ILabels labels,
    IEpics epics,
    IIdentities identities,
    IIssues issues,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public const int MaximumPerRequest = 100;

    public async Task<CreatedIssues> ExecuteAsync(CreateIssuesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = callerIdentity.Caller;

        if (request.Project is null)
        {
            throw Refusal.Validation("project", "A project key is required.");
        }

        if (request.Issues is null || request.Issues.Count == 0)
        {
            throw Refusal.Validation("issues", "At least one issue.");
        }

        if (request.Issues.Count > MaximumPerRequest)
        {
            throw Refusal.Validation("issues", $"At most {MaximumPerRequest} issues in one request.");
        }

        var project = await projects.LiveAsync(request.Project, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var plans = new List<Plan>();
        var refs = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < request.Issues.Count; i++)
        {
            var item = request.Issues[i];
            var field = $"issues[{i}]";

            if (item.Ref is not null && !refs.Add(item.Ref))
            {
                throw Refusal.Validation($"{field}.ref", $"The ref {item.Ref} is used twice.");
            }

            if (item.Status is not null and not (IssueStatus.Todo or IssueStatus.Backlog))
            {
                throw Refusal.Validation($"{field}.status", "An issue is born in todo, or parked in backlog; every other status is an act.");
            }

            if (item.Parent is not null && item.Epic is not null)
            {
                throw new Refusal(RefusalCode.EpicInherited, $"{field}.epic cannot be set on a sub-issue; it follows the parent.");
            }

            plans.Add(new Plan(
                item,
                field,
                Validated.Field($"{field}.title", () => Issue.NormalizeTitle(item.Title!)),
                await labels.ResolveLabelsAsync(project, item.Labels ?? [], $"{field}.labels", cancellationToken),
                item.Epic is null ? null : await EpicAsync(project, item.Epic, $"{field}.epic", cancellationToken),
                item.Assignee is null ? null : await AssigneeAsync(item.Assignee, $"{field}.assignee", cancellationToken)));
        }

        var now = clock.GetUtcNow();

        var created = await transactions.RunAsync(async () =>
        {
            var first = await projects.AllocateIssueNumbersAsync(project.Id, plans.Count, cancellationToken);
            var byRef = new Dictionary<string, Issue>(StringComparer.Ordinal);
            var rows = new List<Issue>();

            foreach (var (plan, offset) in plans.Select((p, offset) => (p, offset)))
            {
                var issue = Issue.Create(project.Id, first + offset, plan.Title, caller.Id, now, parked: plan.Item.Status is IssueStatus.Backlog);
                issue.Describe(plan.Item.Description, now);
                if (plan.Item.Priority is { } priority)
                {
                    Validated.Field($"{plan.Field}.priority", () => { issue.Prioritize(priority, now); return true; });
                }

                issue.SetReady(plan.Item.Ready ?? false, now);
                issue.Assign(plan.Assignee?.Id, now);
                issue.AttachTo(plan.Epic?.Id, now);

                issues.Add(issue);
                history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Created));

                foreach (var label in plan.Labels)
                {
                    issues.Attach(IssueLabel.Attach(issue.Id, label.Id));
                    history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Label, newValue: label.Name));
                }

                if (plan.Epic is { Closed: true })
                {
                    plan.Epic.Reopen(now);
                    history.Add(HistoryEntry.OnEpic(plan.Epic.Id, caller.Id, now, HistoryField.Status, "closed", "open", "reopened by attaching an issue"));
                }

                rows.Add(issue);
                if (plan.Item.Ref is not null)
                {
                    byRef[plan.Item.Ref] = issue;
                }
            }

            await issues.SaveAsync(cancellationToken);

            // Parent links are resolved only after every row exists so refs may
            // point forward in the same bulk request.
            foreach (var (plan, index) in plans.Select((p, index) => (p, index)))
            {
                if (plan.Item.Parent is null)
                {
                    continue;
                }

                var child = rows[index];
                var (parentId, parentKey, localParent) = await ResolveAsync(
                    plan.Item.Parent, byRef, project, $"{plan.Field}.parent", cancellationToken);
                var parent = localParent ?? await issues.LoadForWriteAsync(parentId, cancellationToken)
                    ?? throw Refusal.Validation($"{plan.Field}.parent", $"{parentKey} names no issue.");

                if (parent.ProjectId != project.Id)
                {
                    throw new Refusal(RefusalCode.OtherProject, "A parent and its sub-issue stay in one project.");
                }
                child.AttachToParent(parent.Id, now);
                child.AttachTo(parent.EpicId, now);
                if (plan.Item.Priority is null)
                {
                    child.Prioritize(parent.Priority, now);
                }
                history.Add(HistoryEntry.OnIssue(child.Id, caller.Id, now, HistoryField.Parent, newValue: parentKey));
            }

            await issues.SaveAsync(cancellationToken);

            // Validate after all links are written: otherwise a pair of
            // forward refs could acquire its own parent later in this loop and
            // briefly evade the one-level rule.
            foreach (var child in rows.Where(i => i.ParentId is not null))
            {
                var parent = rows.SingleOrDefault(i => i.Id == child.ParentId)
                    ?? await issues.LoadForWriteAsync(child.ParentId!.Value, cancellationToken)
                    ?? throw new InvalidOperationException("A parent vanished inside the create transaction.");
                if (parent.ParentId is not null || await issues.HasSubIssuesAsync(child.Id, cancellationToken))
                {
                    throw new Refusal(RefusalCode.OneLevel, "Sub-issues are exactly one level deep.");
                }
            }

            // Edges after the rows, so that a ref or a key resolves to a row
            // that exists; a cycle among them is found with the edges written
            // and refuses the whole transaction.
            var edges = new List<(Issue Blocked, Guid BlockerId, string BlockerKey, Issue? BlockerHere)>();
            foreach (var (plan, index) in plans.Select((p, index) => (p, index)))
            {
                var here = rows[index];
                foreach (var target in plan.Item.BlockedBy ?? [])
                {
                    var (id, key, local) = await ResolveAsync(target, byRef, project, $"{plan.Field}.blocked_by", cancellationToken);
                    edges.Add((here, id, key, local));
                }

                foreach (var target in plan.Item.Blocks ?? [])
                {
                    var (id, key, local) = await ResolveAsync(target, byRef, project, $"{plan.Field}.blocks", cancellationToken);
                    if (local is null)
                    {
                        var blocked = await issues.LoadForWriteAsync(id, cancellationToken)
                            ?? throw Refusal.Validation($"{plan.Field}.blocks", $"{key} names no issue.");
                        edges.Add((blocked, here.Id, here.Key(project.Key), here));
                    }
                    else
                    {
                        edges.Add((local, here.Id, here.Key(project.Key), here));
                    }
                }
            }

            foreach (var (blocked, blockerId, blockerKey, _) in edges)
            {
                if (blocked.Id == blockerId)
                {
                    throw Refusal.Validation("issues", $"{blocked.Key(project.Key)} cannot block itself.");
                }

                if (!await issues.HasBlockerAsync(blockerId, blocked.Id, cancellationToken))
                {
                    issues.Add(Blocker.Between(blockerId, blocked.Id, caller.Id, now));
                    history.Add(HistoryEntry.OnIssue(blocked.Id, caller.Id, now, HistoryField.BlockedBy, newValue: blockerKey));
                }
            }

            await issues.SaveAsync(cancellationToken);

            foreach (var (blocked, blockerId, _, _) in edges)
            {
                var cycle = await issues.CycleThroughAsync(blockerId, blocked.Id, cancellationToken);
                if (cycle is not null)
                {
                    throw new Refusal(
                        RefusalCode.Cycle,
                        "The blockers would close a cycle; nothing was created.",
                        new Dictionary<string, object?> { ["path"] = await KeysAsync(cycle.Prepend(blocked.Id), cancellationToken) });
                }
            }

            return rows;
        }, cancellationToken);

        var shapes = new List<IssueShape>();
        foreach (var issue in created)
        {
            var row = await issues.FindLiveAsync(project.Key, issue.Number, cancellationToken)
                ?? throw new InvalidOperationException($"Issue {issue.Number} vanished after its own transaction.");
            shapes.Add(await assembler.CompleteAsync(row, cancellationToken));
        }

        return new CreatedIssues(shapes);
    }

    private async Task<(Guid Id, string Key, Issue? Local)> ResolveAsync(
        string target, Dictionary<string, Issue> byRef, Project project, string field, CancellationToken cancellationToken)
    {
        if (byRef.TryGetValue(target, out var local))
        {
            return (local.Id, local.Key(project.Key), local);
        }

        if (!IssueKey.TryParse(target, out var projectKey, out var number))
        {
            throw Refusal.Validation(field, $"{target} is neither a ref in this request nor an issue key.");
        }

        var row = await issues.FindLiveAsync(projectKey, number, cancellationToken)
            ?? throw Refusal.Validation(field, $"{IssueKey.Of(projectKey, number)} names no issue.");
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        return (row.Id, row.Key, null);
    }

    private async Task<Domain.Epics.Epic> EpicAsync(Project project, string key, string field, CancellationToken cancellationToken)
    {
        if (!EpicKey.TryParse(key, out var projectKey, out var number) || projectKey != project.Key)
        {
            throw Refusal.Validation(field, $"{key} is not an epic key of {project.Key}; an epic and its issues stay in one project.");
        }

        return await epics.FindLiveAsync(project.Id, number, cancellationToken)
            ?? throw Refusal.Validation(field, $"No epic {key}.");
    }

    private async Task<Identity> AssigneeAsync(string name, string field, CancellationToken cancellationToken) =>
        await identities.FindByNameAsync(name, cancellationToken)
        ?? throw Refusal.Validation(field, $"No identity named {name}.");

    private async Task<IReadOnlyList<string>> KeysAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToArray();
        var rows = (await issues.FindLiveManyAsync(list, cancellationToken)).ToDictionary(r => r.Id);
        return [.. list.Select(id => rows.TryGetValue(id, out var row) ? row.Key : id.ToString())];
    }

    private sealed record Plan(
        NewIssue Item,
        string Field,
        string Title,
        IReadOnlyList<Label> Labels,
        Domain.Epics.Epic? Epic,
        Identity? Assignee);
}

internal static class IssueKeys
{
    public static string Key(this Issue issue, string projectKey) => IssueKey.Of(projectKey, issue.Number);
}
