using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Application.Acts;

public sealed record CloseRequest(IssueStatus? Status, string? Result);

public sealed record ReviewRequest(string? Result);

public sealed record ReopenRequest(string? Comment);

/// <summary>
/// The line every act on a claimed issue runs through (<c>docs/api.md</c>, The
/// acts on an issue): a user acts over any claim; an agent that is not the
/// holder is told <c>claim-lost</c> when the newest claim entry names it as the
/// one displaced, and <c>claim-held</c> otherwise.
/// </summary>
public static class ClaimGate
{
    public static async Task RefuseIfHeldByAnotherAsync(
        IssueRow row, Caller caller, IHistory history, IIdentities identities, CancellationToken cancellationToken)
    {
        if (row.ClaimedBy is not { } holder || holder == caller.Id || caller.IsUser)
        {
            return;
        }

        var last = await history.LastAsync(row.Id, HistoryField.Claim, cancellationToken);
        var lost = last is { OldValue: { } previous } && previous == caller.Id.ToString();
        var holderRef = await identities.FindAsync(holder, cancellationToken);

        throw new Refusal(
            lost ? RefusalCode.ClaimLost : RefusalCode.ClaimHeld,
            lost
                ? $"Your claim on {row.Key} lapsed and {holderRef?.Name ?? "somebody else"} holds it now."
                : $"{row.Key} is held by {holderRef?.Name ?? "somebody else"}.",
            new Dictionary<string, object?> { ["holder"] = holderRef is null ? null : IdentityRef.Of(holderRef) });
    }
}

/// <summary>
/// The moves of the transition table other than claim and release: close,
/// review, reopen — acts in Domain (ADR 0016), one transaction each here, with
/// the history the table implies.
/// </summary>
public sealed class MoveIssue(
    ICallerIdentity callerIdentity,
    IProjects projects,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    IReleases releases,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public Task<IssueShape> CloseAsync(string key, CloseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = request.Status ?? throw Refusal.Validation("status", "A close is done or canceled.");

        return OnAsync(key, async (issue, row, caller, now) =>
        {
            var project = await projects.FindByIdAsync(row.ProjectId, cancellationToken)
                ?? throw new InvalidOperationException($"Issue {row.Key} has no project row.");

            var hadResult = issue.Result;
            var holder = issue.Claim?.HolderId;
            var landed = issue.Close(target, request.Result, caller.Kind, project.ReviewRequired, now);

            if (landed is IssueStatus.Done)
            {
                await releases.AddDoneAsync(issue, cancellationToken);
            }

            History(issue, row.Status, landed, holder, hadResult, caller, now);
        }, cancellationToken);
    }

    public Task<IssueShape> ReviewAsync(string key, ReviewRequest request, CancellationToken cancellationToken) =>
        OnAsync(key, (issue, row, caller, now) =>
        {
            var hadResult = issue.Result;
            var holder = issue.Claim?.HolderId;
            issue.HandIn(request?.Result, now);

            History(issue, row.Status, IssueStatus.Review, holder, hadResult, caller, now);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <remarks>
    /// The comment is written first, and expected on the way back from
    /// <c>review</c> — pointed out by the CLI when missing, never refused.
    /// </remarks>
    public Task<IssueShape> ReopenAsync(string key, ReopenRequest request, CancellationToken cancellationToken) =>
        OnAsync(key, async (issue, row, caller, now) =>
        {
            issue.Reopen(now);
            await releases.RemoveFromOpenAsync(issue.Id, cancellationToken);

            if (!string.IsNullOrWhiteSpace(request?.Comment))
            {
                issues.Add(Comment.Write(issue.Id, caller.Id, request.Comment, now));
            }

            history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Status, ClaimHistory.SnakeCase(row.Status), ClaimHistory.SnakeCase(IssueStatus.Todo)));
        }, cancellationToken, gate: false);

    private void History(Issue issue, IssueStatus from, IssueStatus to, Guid? holder, string? hadResult, Caller caller, DateTimeOffset now)
    {
        if (holder is { } released)
        {
            history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Claim, released.ToString(), null));
        }

        if (issue.Result != hadResult)
        {
            history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Result));
        }

        history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Status, ClaimHistory.SnakeCase(from), ClaimHistory.SnakeCase(to)));
    }

    private async Task<IssueShape> OnAsync(
        string key,
        Func<Issue, IssueRow, Caller, DateTimeOffset, Task> move,
        CancellationToken cancellationToken,
        bool gate = true)
    {
        var caller = callerIdentity.Caller;
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        if (gate)
        {
            await ClaimGate.RefuseIfHeldByAnotherAsync(row, caller, history, identities, cancellationToken);
        }

        await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

            await move(issue, row, caller, clock.GetUtcNow());
            await issues.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);

        var after = await issues.FindLiveAsync(row.ProjectKey, row.Number, cancellationToken)
            ?? throw new InvalidOperationException($"Issue {row.Key} vanished under its own move.");

        return await assembler.CompleteAsync(after, cancellationToken);
    }
}
