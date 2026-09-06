using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Acts;

/// <summary>The filters of <c>next</c>, as query parameters on GET and as a body on POST.</summary>
public sealed record NextRequest(bool? Ready, string? Epic, IReadOnlyList<string>? Label, string? Repo, int? Limit, int? Wait);

/// <summary>What <c>GET /projects/{key}/next</c> answers: the "ready for agents" list, and why the rest is not on it.</summary>
public sealed record NextPage(IReadOnlyList<IssueSummaryShape> Items, int Total, bool HasMore, Reasons Reasons);

/// <summary>What <c>POST /projects/{key}/next</c> answers: the issue taken, or nothing and why.</summary>
public sealed record NextAnswer(IssueShape? Issue, Reasons Reasons);

/// <summary>
/// The question the product exists to answer (VISION 10): what would the caller
/// be handed, in that order — and, as the act, hand it over and claim it in one
/// transaction that cannot be split.
/// </summary>
public sealed class Next(
    ICallerIdentity callerIdentity,
    IProjects projects,
    ProjectScope scope,
    ILabels labels,
    IEpics epics,
    IIssues issues,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    IChanges changes,
    InstanceSettings settings,
    TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaximumWaitSeconds = Waits.MaximumSeconds;

    public async Task<NextPage> PreviewAsync(string projectKey, NextRequest request, CancellationToken cancellationToken)
    {
        var limit = request.Limit ?? DefaultLimit;
        if (limit < 1 || limit > ListIssues.MaximumLimit)
        {
            throw Refusal.Validation("limit", $"limit is 1 to {ListIssues.MaximumLimit}.");
        }

        var query = await QueryAsync(projectKey, request, cancellationToken);
        var ids = await issues.NextWorkableAsync(query, limit + 1, lockOne: false, cancellationToken);
        var hasMore = ids.Count > limit;
        var page = hasMore ? ids.Take(limit).ToArray() : [.. ids];

        var rows = (await issues.FindLiveManyAsync(page, cancellationToken)).ToDictionary(r => r.Id);
        var ordered = page.Select(id => rows[id]).ToArray();

        return new NextPage(
            await assembler.SummariesAsync(ordered, cancellationToken),
            await issues.CountWorkableAsync(query, cancellationToken),
            hasMore,
            await issues.ReasonsAsync(query, cancellationToken));
    }

    public async Task<NextAnswer> TakeAsync(string projectKey, NextRequest request, CancellationToken cancellationToken)
    {
        Waits.Validate(request.Wait);

        var query = await QueryAsync(projectKey, request, cancellationToken);
        if (request.Wait is null)
        {
            return await TakeOnceAsync(query, cancellationToken);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.Wait.Value));
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        while (true)
        {
            // LISTEN, then register this pulse, then look. Otherwise a commit
            // between an empty query and LISTEN could be missed until the deadline.
            try
            {
                await changes.EnsureListeningAsync(query.ProjectId, waiting.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return await TakeOnceAsync(query, cancellationToken);
            }
            var changed = changes.WaitAsync(query.ProjectId, waiting.Token);
            var answer = await TakeOnceAsync(query, cancellationToken);
            if (answer.Issue is not null)
            {
                await waiting.CancelAsync();
                return answer;
            }

            try
            {
                await changed;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return answer;
            }
        }
    }

    private async Task<NextAnswer> TakeOnceAsync(NextQuery query, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;

        var taken = await transactions.RunAsync(async () =>
        {
            var ids = await issues.NextWorkableAsync(query, 1, lockOne: true, cancellationToken);
            if (ids.Count == 0)
            {
                return (Guid?)null;
            }

            var issue = await issues.LoadForWriteAsync(ids[0], cancellationToken)
                ?? throw new InvalidOperationException("The issue next locked is gone.");

            var before = issue.Status;
            var now = clock.GetUtcNow();
            var outcome = issue.ClaimFor(caller.Id, caller.Kind, force: false, now, settings.ClaimExpiry);
            ClaimHistory.Write(history, issue, before, outcome, caller.Id, now);

            await issues.SaveAsync(cancellationToken);
            return issue.Id;
        }, cancellationToken);

        var reasons = await issues.ReasonsAsync(query, cancellationToken);
        if (taken is null)
        {
            return new NextAnswer(null, reasons);
        }

        var row = (await issues.FindLiveManyAsync([taken.Value], cancellationToken)).Single();
        return new NextAnswer(await assembler.CompleteAsync(row, cancellationToken), reasons);
    }

    private async Task<NextQuery> QueryAsync(string projectKey, NextRequest request, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var project = await projects.LiveAsync(projectKey, settings, cancellationToken);
        await scope.RequireAsync(project.Id, cancellationToken);
        var live = (await labels.ListAsync(project.Id, cancellationToken)).ToDictionary(l => l.Name, StringComparer.Ordinal);

        Guid? epicId = null;
        if (request.Epic is not null)
        {
            if (!EpicKey.TryParse(request.Epic, out var epicProject, out var number) || epicProject != project.Key)
            {
                throw Refusal.Validation("epic", $"{request.Epic} is not an epic key of {project.Key}.");
            }

            epicId = (await epics.FindLiveAsync(project.Id, number, cancellationToken))?.Id
                ?? throw Refusal.Validation("epic", $"No epic {request.Epic}.");
        }

        foreach (var name in request.Label ?? [])
        {
            if (!live.ContainsKey(name))
            {
                throw new Refusal(RefusalCode.UnknownLabel, $"Project {project.Key} has no label {name}.", new Dictionary<string, object?> { ["label"] = name });
            }
        }

        if (request.Repo is not null && (!live.TryGetValue(request.Repo, out var repo) || repo.Group != Label.RepoGroup))
        {
            throw new Refusal(
                RefusalCode.UnknownLabel,
                $"Project {project.Key} has no label {request.Repo} in the group {Label.RepoGroup}; the .hostingaffe file names a label the project does not have (VISION 13).",
                new Dictionary<string, object?> { ["label"] = request.Repo });
        }

        return new NextQuery(
            project.Id,
            caller.Id,
            project.TriageRequired || request.Ready is true,
            epicId,
            request.Label ?? [],
            request.Repo);
    }
}
