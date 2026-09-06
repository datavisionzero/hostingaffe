using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The four edge acts of <c>docs/api.md</c>: one label on or off, one blocker
/// on or off, each returning the complete issue and writing history.
/// </summary>
public sealed class IssueEdges(
    ICallerIdentity callerIdentity,
    ProjectScope scope,
    IProjects projects,
    ILabels labels,
    IIssues issues,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    /// <summary>Add one label, replacing another of its group in the same transaction, under the issue's lock.</summary>
    public Task<IssueShape> AddLabelAsync(string key, string labelName, CancellationToken cancellationToken) =>
        OnAsync(key, async (issue, row, now) =>
        {
            var project = await projects.FindByIdAsync(row.ProjectId, cancellationToken) ?? throw new InvalidOperationException("No project row.");
            var label = await labels.FindAsync(project.Id, labelName.Trim(), cancellationToken);
            if (label is null || label.Deleted)
            {
                throw new Refusal(RefusalCode.UnknownLabel, $"Project {project.Key} has no label {labelName}.", new Dictionary<string, object?> { ["label"] = labelName });
            }

            var current = (await issues.LabelsOfAsync([issue.Id], cancellationToken)).Select(l => l.Label).ToList();
            if (current.Any(c => c.Id == label.Id))
            {
                return;
            }

            foreach (var other in current.Where(c => label.Group is not null && c.Group == label.Group))
            {
                await issues.DetachAsync(issue.Id, other.Id, cancellationToken);
                history.Add(HistoryEntry.OnIssue(issue.Id, callerIdentity.Caller.Id, now, HistoryField.Label, oldValue: other.Name));
            }

            issues.Attach(IssueLabel.Attach(issue.Id, label.Id));
            history.Add(HistoryEntry.OnIssue(issue.Id, callerIdentity.Caller.Id, now, HistoryField.Label, newValue: label.Name));
            issue.Touch(now);
        }, cancellationToken);

    public Task<IssueShape> RemoveLabelAsync(string key, string labelName, CancellationToken cancellationToken) =>
        OnAsync(key, async (issue, row, now) =>
        {
            var current = (await issues.LabelsOfAsync([issue.Id], cancellationToken)).Select(l => l.Label).ToList();
            var label = current.FirstOrDefault(c => c.Name == labelName.Trim());
            if (label is null)
            {
                return;
            }

            await issues.DetachAsync(issue.Id, label.Id, cancellationToken);
            history.Add(HistoryEntry.OnIssue(issue.Id, callerIdentity.Caller.Id, now, HistoryField.Label, oldValue: label.Name));
            issue.Touch(now);
        }, cancellationToken);

    /// <summary>Add a blocker, across projects if need be; <c>cycle</c> when it closes one.</summary>
    public Task<IssueShape> AddBlockerAsync(string key, string blockerKey, CancellationToken cancellationToken) =>
        OnAsync(key, async (issue, row, now) =>
        {
            var blocker = await issues.LiveAsync(blockerKey, settings, cancellationToken);
            await scope.RequireAsync(blocker.ProjectId, cancellationToken);
            if (blocker.Id == issue.Id)
            {
                throw new Refusal(RefusalCode.Cycle, $"{row.Key} cannot block itself.", new Dictionary<string, object?> { ["path"] = new[] { row.Key, row.Key } });
            }

            if (await issues.HasBlockerAsync(blocker.Id, issue.Id, cancellationToken))
            {
                return;
            }

            issues.Add(Blocker.Between(blocker.Id, issue.Id, callerIdentity.Caller.Id, now));
            await issues.SaveAsync(cancellationToken);

            var cycle = await issues.CycleThroughAsync(blocker.Id, issue.Id, cancellationToken);
            if (cycle is not null)
            {
                var rows = (await issues.FindLiveManyAsync(cycle, cancellationToken)).ToDictionary(r => r.Id);
                throw new Refusal(
                    RefusalCode.Cycle,
                    $"{blocker.Key} is itself blocked by {row.Key}; the edge would close a cycle.",
                    new Dictionary<string, object?> { ["path"] = cycle.Prepend(issue.Id).Select(id => rows.TryGetValue(id, out var r) ? r.Key : id.ToString()).ToArray() });
            }

            history.Add(HistoryEntry.OnIssue(issue.Id, callerIdentity.Caller.Id, now, HistoryField.BlockedBy, newValue: blocker.Key));
            issue.Touch(now);
        }, cancellationToken);

    public Task<IssueShape> RemoveBlockerAsync(string key, string blockerKey, CancellationToken cancellationToken) =>
        OnAsync(key, async (issue, row, now) =>
        {
            if (!IssueKey.TryParse(blockerKey, out var projectKey, out var number))
            {
                throw new Refusal(RefusalCode.NotFound, $"{blockerKey} is not an issue key.");
            }

            var blocker = await issues.FindLiveAsync(projectKey, number, cancellationToken)
                ?? await issues.FindDeletedAsync(projectKey, number, cancellationToken);
            if (blocker is not null)
                await scope.RequireAsync(blocker.ProjectId, cancellationToken);
            if (blocker is null || !await issues.HasBlockerAsync(blocker.Id, issue.Id, cancellationToken))
            {
                return;
            }

            await issues.RemoveBlockerAsync(blocker.Id, issue.Id, cancellationToken);
            history.Add(HistoryEntry.OnIssue(issue.Id, callerIdentity.Caller.Id, now, HistoryField.BlockedBy, oldValue: blocker.Key));
            issue.Touch(now);
        }, cancellationToken);

    private async Task<IssueShape> OnAsync(
        string key, Func<Issue, IssueRow, DateTimeOffset, Task> change, CancellationToken cancellationToken)
    {
        var row = await issues.LiveAsync(key, settings, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

            var now = clock.GetUtcNow();
            await change(issue, row, now);
            issue.ExtendClaimIfHeldBy(callerIdentity.Caller.Id, callerIdentity.Caller.Kind, now, settings.ClaimExpiry);
            await issues.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);

        var after = await issues.FindLiveAsync(row.ProjectKey, row.Number, cancellationToken)
            ?? throw new InvalidOperationException($"Issue {row.Key} vanished under its own write.");

        return await assembler.CompleteAsync(after, cancellationToken);
    }
}
