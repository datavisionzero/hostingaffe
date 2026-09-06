using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>One issue, complete: the context package (ADR 0012).</summary>
public sealed class ReadIssue(IIssues issues, ProjectScope scope, IssueAssembler assembler, InstanceSettings settings)
{
    public async Task<IssueShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);
        return await assembler.CompleteAsync(row, cancellationToken);
    }
}

/// <summary>The query string of <c>GET /issues</c>, as strings; the act makes sense of them.</summary>
public sealed record IssueListRequest(
    string? Project,
    IReadOnlyList<string> Status,
    bool? Ready,
    int? PriorityMin,
    int? PriorityMax,
    IReadOnlyList<string> Label,
    string? Epic,
    string? Assignee,
    string? Claimed,
    string? Author,
    bool? Blocked,
    bool? HasOpenQuestion,
    string? Search,
    bool? Deleted,
    string? Sort,
    string? Order,
    string? Cursor,
    int? Limit,
    int? Wait);

/// <summary>The page and its validator, or an unchanged long-poll answer.</summary>
public sealed record IssuePageAnswer(IssuePage? Page, string ETag);

/// <summary>
/// A page of slim issues (ADR 0012): every filter of <c>docs/api.md</c>, four
/// sorts, a cursor rather than an offset — because agents insert while others
/// read, and an offset would skip or repeat under them.
/// </summary>
public sealed class ListIssues(
    ICallerIdentity callerIdentity,
    IProjects projects,
    IEpics epics,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    IssueAssembler assembler,
    InstanceSettings settings,
    IChanges changes)
{
    public const int DefaultLimit = 50;

    public const int MaximumLimit = 200;

    public async Task<IssuePage> ExecuteAsync(IssueListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var caller = callerIdentity.Caller;

        var limit = request.Limit ?? DefaultLimit;
        if (limit < 1 || limit > MaximumLimit)
        {
            throw Refusal.Validation("limit", $"limit is 1 to {MaximumLimit}; larger pages are refused, not truncated (ADR 0012).");
        }

        var sort = request.Sort?.ToLowerInvariant() switch
        {
            null or "updated" => IssueSort.Updated,
            "created" => IssueSort.Created,
            "priority" => IssueSort.Priority,
            "epic" => IssueSort.Epic,
            _ => throw Refusal.Validation("sort", "sort is updated, created, priority or epic."),
        };

        var order = request.Order?.ToLowerInvariant() switch
        {
            null when sort is IssueSort.Updated => SortOrder.Desc,
            null => SortOrder.Asc,
            "asc" => SortOrder.Asc,
            "desc" => SortOrder.Desc,
            _ => throw Refusal.Validation("order", "order is asc or desc."),
        };

        var query = await QueryAsync(request, caller, cancellationToken);
        var after = request.Cursor is null ? null : IssueCursor.Decode(request.Cursor, query, sort, order);

        var page = await issues.ListAsync(query, sort, order, after, limit, cancellationToken);
        var items = await assembler.SummariesAsync(page.Items, cancellationToken);

        return new IssuePage(
            items,
            page.Total,
            page.HasMore,
            page.HasMore ? IssueCursor.Encode(query, sort, order, page.Items[^1]) : null);
    }

    /// <summary>
    /// The same page, optionally held until it differs from the one the caller
    /// already has. The wake channel is the project's and carries no state, so
    /// every wake-up asks this list its original question again — a change
    /// somewhere else in the project wakes it, finds the same validator, and is
    /// answered <c>304</c>. That is the price of a stateless channel and it is
    /// the right one.
    /// </summary>
    public async Task<IssuePageAnswer> WaitAsync(
        IssueListRequest request, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Waits.Validate(request.Wait);
        if (request.Wait is null)
        {
            var immediate = await ExecuteAsync(request, cancellationToken);
            return new IssuePageAnswer(immediate, Waits.ETag(immediate));
        }

        // The channel is a project's. A list across every project the caller
        // sees has no one channel to hang on, and holding one connection per
        // project to fake it would put the cost of that decision on the
        // instance without saying so. A waiting list names its project.
        if (request.Project is null)
        {
            throw Refusal.Validation("wait", "wait needs project: the wake channel is one project's (docs/api.md, Waiting).");
        }

        var project = await projects.LiveAsync(request.Project, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.Wait.Value));
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        string? baseline = ifNoneMatch;

        while (true)
        {
            try
            {
                await changes.EnsureListeningAsync(project.Id, waiting.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return await AtDeadlineAsync(request, baseline, cancellationToken);
            }

            var changed = changes.WaitAsync(project.Id, waiting.Token);
            var page = await ExecuteAsync(request, cancellationToken);
            var tag = Waits.ETag(page);

            if (baseline is null && page.Items.Count > 0 || baseline is not null && !Waits.Matches(baseline, tag))
            {
                await waiting.CancelAsync();
                return new IssuePageAnswer(page, tag);
            }

            baseline ??= tag;
            try
            {
                await changed;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return await AtDeadlineAsync(request, baseline, cancellationToken);
            }
        }
    }

    private async Task<IssuePageAnswer> AtDeadlineAsync(
        IssueListRequest request, string? baseline, CancellationToken cancellationToken)
    {
        var page = await ExecuteAsync(request, cancellationToken);
        var tag = Waits.ETag(page);
        return baseline is not null && Waits.Matches(baseline, tag)
            ? new IssuePageAnswer(null, tag)
            : new IssuePageAnswer(page, tag);
    }

    private async Task<IssueQuery> QueryAsync(IssueListRequest request, Caller caller, CancellationToken cancellationToken)
    {
        Project? project = null;
        if (request.Project is not null)
        {
            project = await projects.LiveAsync(request.Project, settings, cancellationToken);
            await scope.RequireAsync(project.Id, cancellationToken);
        }

        var statuses = new List<IssueStatus>();
        foreach (var status in request.Status)
        {
            statuses.Add(Enum.TryParse<IssueStatus>(status.Replace("_", string.Empty), ignoreCase: true, out var parsed)
                ? parsed
                : throw Refusal.Validation("status", $"{status} is not a status."));
        }

        Guid? epicId = null;
        var epicNone = false;
        if (request.Epic is not null)
        {
            if (request.Epic.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                epicNone = true;
            }
            else if (EpicKey.TryParse(request.Epic, out var epicProject, out var number))
            {
                var owner = await projects.FindByKeyAsync(epicProject, cancellationToken)
                    ?? throw Refusal.Validation("epic", $"No project {epicProject}.");
                await scope.RequireAsync(owner.Id, cancellationToken);
                epicId = (await epics.FindLiveAsync(owner.Id, number, cancellationToken))?.Id
                    ?? throw Refusal.Validation("epic", $"No epic {request.Epic}.");
            }
            else
            {
                throw Refusal.Validation("epic", $"{request.Epic} is not an epic key.");
            }
        }

        var (assigneeId, assigneeNone) = await IdentityFilterAsync(request.Assignee, "assignee", caller, allowNone: true, cancellationToken);

        bool? claimedAtAll = null;
        Guid? claimedBy = null;
        if (request.Claimed is not null)
        {
            if (bool.TryParse(request.Claimed, out var flag))
            {
                claimedAtAll = flag;
            }
            else
            {
                (claimedBy, _) = await IdentityFilterAsync(request.Claimed, "claimed", caller, allowNone: false, cancellationToken);
            }
        }

        var (authorId, _) = await IdentityFilterAsync(request.Author, "author", caller, allowNone: false, cancellationToken);

        return new IssueQuery(
            await scope.ProjectIdsAsync(cancellationToken),
            project?.Id,
            statuses,
            request.Ready,
            Priority(request.PriorityMin, "priority_min"),
            Priority(request.PriorityMax, "priority_max"),
            request.Label,
            epicId,
            epicNone,
            assigneeId,
            assigneeNone,
            claimedAtAll,
            claimedBy,
            authorId,
            request.Blocked,
            request.HasOpenQuestion,
            Search(request.Search),
            request.Deleted ?? false);
    }

    private async Task<(Guid? Id, bool None)> IdentityFilterAsync(
        string? value, string field, Caller caller, bool allowNone, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return (null, false);
        }

        if (allowNone && value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        if (value.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            return (caller.Id, false);
        }

        var identity = await identities.FindByNameAsync(value, cancellationToken)
            ?? throw Refusal.Validation(field, $"No identity named {value}.");

        return (identity.Id, false);
    }

    private static Priority? Priority(int? value, string field) =>
        value is null
            ? null
            : value is >= 0 and <= 4
                ? (Priority)value
                : throw Refusal.Validation(field, "Priority is 0 to 4.");

    private static string? Search(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
