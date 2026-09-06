using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// Turns rows into the two issue shapes, a page at a time: one query per
/// attachment kind for the whole page rather than one per issue.
/// </summary>
public sealed class IssueAssembler(
    IIssues issues,
    IIdentities identities,
    IEpics epics,
    IProjects projects,
    IPages pages,
    ILabels labels,
    IReleases releases,
    ProjectScope scope)
{
    public async Task<IReadOnlyList<IssueSummaryShape>> SummariesAsync(
        IReadOnlyList<IssueRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToArray();
        var labelRows = await issues.LabelsOfAsync(ids, cancellationToken);
        var blockers = await issues.BlockersOfAsync(ids, cancellationToken);
        var questions = await issues.OpenQuestionCountsAsync(ids, cancellationToken);
        var children = await issues.OpenSubIssueCountsAsync(ids, cancellationToken);
        var parents = (await issues.FindLiveManyAsync(rows.Select(r => r.ParentId).OfType<Guid>(), cancellationToken)).ToDictionary(r => r.Id);
        var epicKeys = await EpicKeysAsync(rows.Select(r => r.EpicId), cancellationToken);
        var releaseNames = await releases.CurrentNamesAsync(ids, cancellationToken);
        var people = await identities.FindManyAsync(
            rows.SelectMany(r => new[] { r.AssigneeId, r.ClaimedBy, r.DeletedBy }).OfType<Guid>().Distinct(),
            cancellationToken);
        var allowedProjects = await scope.ProjectIdsAsync(cancellationToken);

        return
        [
            .. rows.Select(row =>
            {
                var blockedBy = blockers.Where(e => e.NearId == row.Id).Select(e => e.Far).ToArray();
                return new IssueSummaryShape(
                    row.Key,
                    row.ProjectKey,
                    row.Title,
                    row.Status,
                    row.Ready,
                    row.Priority,
                    [.. labelRows.Where(l => l.IssueId == row.Id).Select(l => l.Label.Name).Order(StringComparer.Ordinal)],
                    row.EpicId is { } epicId ? epicKeys.GetValueOrDefault(epicId) : null,
                    row.ParentId is { } parentId && parents.TryGetValue(parentId, out var parent) ? parent.Key : null,
                    releaseNames.GetValueOrDefault(row.Id),
                    Ref(people, row.AssigneeId),
                    Claim(people, row),
                    [.. blockedBy.Select(b => allowedProjects.Contains(b.ProjectId)
                        ? new BlockerRefShape(b.Key, !b.Closed)
                        : new BlockerRefShape(null, true))],
                    questions.GetValueOrDefault(row.Id),
                    blockedBy.Count(b => !b.Closed),
                    children.GetValueOrDefault(row.Id),
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.ClosedAt,
                    row.DeletedAt,
                    Ref(people, row.DeletedBy));
            }),
        ];
    }

    public async Task<IssueShape> CompleteAsync(IssueRow row, CancellationToken cancellationToken)
    {
        Guid[] ids = [row.Id];
        var labelRows = await issues.LabelsOfAsync(ids, cancellationToken);
        var blockedBy = (await issues.BlockersOfAsync(ids, cancellationToken)).Select(e => e.Far).ToArray();
        var blocks = (await issues.BlockedByEachAsync(ids, cancellationToken)).Select(e => e.Far).ToArray();
        var comments = await issues.CommentsOfAsync(row.Id, cancellationToken);
        var questions = await issues.QuestionsOfAsync(row.Id, cancellationToken);
        var epic = row.EpicId is { } epicId ? await epics.FindAsync(epicId, cancellationToken) : null;
        var parent = row.ParentId is { } parentId ? (await issues.FindLiveManyAsync([parentId], cancellationToken)).SingleOrDefault() : null;
        var subIssues = await issues.SubIssuesOfAsync(row.Id, cancellationToken);
        var project = await projects.FindByKeyAsync(row.ProjectKey, cancellationToken)
            ?? throw new InvalidOperationException($"Issue {row.Key} has no project row.");
        var projectLabels = await labels.ListAsync(project.Id, cancellationToken);

        // The project's instructions travel with the ticket, which is the whole
        // of VISION 15.3: one page, delivered wherever the package is, and never
        // a second route an agent has to know about. A designated page that is
        // deleted resolves to nothing here and comes back with its restore.
        var instructions = project.InstructionsPageId is { } instructionsId
            ? (await pages.FindLiveManyAsync([instructionsId], cancellationToken)).SingleOrDefault()
            : null;
        var releaseNames = await releases.CurrentNamesAsync([row.Id], cancellationToken);
        var allowedProjects = await scope.ProjectIdsAsync(cancellationToken);

        var people = await identities.FindManyAsync(
            new[] { row.AuthorId, row.AssigneeId, row.ClaimedBy }
                .Concat(comments.Select(c => (Guid?)c.AuthorId))
                .Concat(questions.SelectMany(q => new[] { (Guid?)q.AskedBy, q.AnsweredBy }))
                .OfType<Guid>()
                .Distinct(),
            cancellationToken);

        return new IssueShape(
            row.Key,
            row.ProjectKey,
            row.Title,
            row.Description,
            row.Result,
            row.Status,
            row.Ready,
            row.Priority,
            [.. labelRows.Select(l => LabelShape.Of(l.Label)).OrderBy(l => l.Name, StringComparer.Ordinal)],
            epic is null ? null : new EpicRefShape(EpicKey.Of(row.ProjectKey, epic.Number), epic.Title, epic.Description, epic.Status),
            parent is null ? null : Ref(parent),
            releaseNames.GetValueOrDefault(row.Id),
            [.. subIssues.Select(Ref)],
            Ref(people, row.AssigneeId),
            Claim(people, row),
            Ref(people, row.AuthorId)!,
            [.. blockedBy.Select(issue => Link(issue, allowedProjects, outcome: true))],
            [.. blocks.Select(issue => Link(issue, allowedProjects, outcome: false))],
            questions.Count(q => q.Open),
            blockedBy.Count(b => !b.Closed),
            subIssues.Count(i => !i.Closed),
            [.. comments.Select(c => new CommentShape(c.Id, Ref(people, c.AuthorId)!, c.Body, c.CreatedAt, c.EditedAt))],
            [.. questions.Select(q => new QuestionShape(q.Id, q.Text, Ref(people, q.AskedBy)!, q.AskedAt, q.Answer, Ref(people, q.AnsweredBy), q.AnsweredAt))],
            new ProjectContextShape(
                project.Key,
                project.Name,
                project.TriageRequired,
                project.ReviewRequired,
                [.. projectLabels.Select(LabelShape.Of)],
                instructions is null ? null : new InstructionsShape(instructions.Slug, instructions.Title, instructions.Body)),
            row.CreatedAt,
            row.UpdatedAt,
            row.ClosedAt);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> EpicKeysAsync(IEnumerable<Guid?> epicIds, CancellationToken cancellationToken)
    {
        var ids = epicIds.OfType<Guid>().Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var found = await epics.FindManyAsync(ids, cancellationToken);
        var projectKeys = new Dictionary<Guid, string>();
        foreach (var projectId in found.Select(e => e.ProjectId).Distinct())
        {
            var project = await projects.FindByIdAsync(projectId, cancellationToken);
            if (project is not null)
            {
                projectKeys[projectId] = project.Key;
            }
        }

        return found.ToDictionary(e => e.Id, e => EpicKey.Of(projectKeys[e.ProjectId], e.Number));
    }

    /// <summary>
    /// One end of a blocker edge. <paramref name="outcome"/> carries the far
    /// issue's result, which is the last piece of the context package (VISION
    /// 15.5) and belongs on the blockers alone: an agent starts from what its
    /// predecessors decided, and a successor has decided nothing yet. An issue
    /// in a project the caller does not reach stays what it always was here —
    /// hidden and assumed open — and its result is no exception.
    /// </summary>
    private static BlockerLinkShape Link(IssueRow far, IReadOnlySet<Guid> allowedProjects, bool outcome) =>
        allowedProjects.Contains(far.ProjectId)
            ? new(far.Key, far.Title, far.Status, !far.Closed, outcome ? far.Result : null)
            : new(null, null, null, true);

    private static IssueRefShape Ref(IssueRow issue) => new(issue.Key, issue.Title);

    private static IdentityRef? Ref(IReadOnlyDictionary<Guid, Identity> people, Guid? id) =>
        id is { } known && people.TryGetValue(known, out var identity) ? IdentityRef.Of(identity) : null;

    private static ClaimShape? Claim(IReadOnlyDictionary<Guid, Identity> people, IssueRow row) =>
        row.ClaimedBy is { } holder && Ref(people, holder) is { } holderRef
            ? new ClaimShape(holderRef, row.ClaimedAt!.Value, row.ClaimExpiresAt)
            : null;
}
