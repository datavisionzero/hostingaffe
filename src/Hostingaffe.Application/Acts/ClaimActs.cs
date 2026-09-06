using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Application.Acts;

public sealed record ClaimRequest(bool? Force);

/// <summary>
/// <c>POST /issues/{key}/claim</c>: the claim by key, under the row's lock, so
/// that two claimants at once produce exactly one winner and the other a clear
/// refusal (VISION 11). The rule is the Domain's; this is the transaction, the
/// history and the shape.
/// </summary>
public sealed class ClaimIssue(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<IssueShape> ExecuteAsync(string key, bool force, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        try
        {
            await transactions.RunAsync(async () =>
            {
                var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                    ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

                var before = issue.Status;
                var now = clock.GetUtcNow();
                var outcome = issue.ClaimFor(caller.Id, caller.Kind, force, now, settings.ClaimExpiry);
                ClaimHistory.Write(history, issue, before, outcome, caller.Id, now);

                await issues.SaveAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }
        catch (Refusal refusal) when (refusal.Code is RefusalCode.ClaimHeld or RefusalCode.ClaimProtected
                                      && refusal.Extensions.TryGetValue("holder", out var id) && id is Guid holderId)
        {
            throw await WithHolderAsync(refusal, holderId, cancellationToken);
        }

        return await assembler.CompleteAsync(
            await issues.FindLiveAsync(row.ProjectKey, row.Number, cancellationToken)
                ?? throw new InvalidOperationException($"Issue {row.Key} vanished under its own claim."),
            cancellationToken);
    }

    private async Task<Refusal> WithHolderAsync(Refusal refusal, Guid holderId, CancellationToken cancellationToken)
    {
        var holder = await identities.FindAsync(holderId, cancellationToken);
        var extensions = new Dictionary<string, object?>(refusal.Extensions)
        {
            ["holder"] = holder is null ? null : IdentityRef.Of(holder),
        };

        return new Refusal(refusal.Code, holder is null ? refusal.Detail : $"{refusal.Detail} Held by {holder.Name}.", extensions);
    }

    internal static string SnakeCase(IssueStatus status) => ClaimHistory.SnakeCase(status);
}

/// <summary>What a claim writes into the history, the same for `claim` and for `next`.</summary>
internal static class ClaimHistory
{
    public static void Write(IHistory history, Issue issue, IssueStatus before, ClaimOutcome outcome, Guid caller, DateTimeOffset now)
    {
        if (outcome.Kind is ClaimOutcomeKind.Extended)
        {
            return;
        }

        history.Add(HistoryEntry.OnIssue(
            issue.Id, caller, now, HistoryField.Claim,
            outcome.PreviousHolder?.ToString(), caller.ToString(),
            outcome.Kind switch
            {
                ClaimOutcomeKind.TakenAfterExpiry => HistoryNote.Expired,
                ClaimOutcomeKind.Forced => HistoryNote.Forced,
                _ => null,
            }));

        if (before is not IssueStatus.InProgress)
        {
            history.Add(HistoryEntry.OnIssue(issue.Id, caller, now, HistoryField.Status, SnakeCase(before), SnakeCase(IssueStatus.InProgress)));
        }
    }

    public static string SnakeCase(IssueStatus status) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString());
}

/// <summary>
/// <c>POST /issues/{key}/release</c>: let go. Only the holder, or a user — a
/// human's word is not stopped by an agent's hold. An agent that is not the
/// holder is told <c>claim-lost</c> when its claim lapsed and was taken, and
/// <c>claim-held</c> otherwise.
/// </summary>
public sealed class ReleaseIssue(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    IHistory history,
    ITransactions transactions,
    IssueAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<IssueShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        await ClaimGate.RefuseIfHeldByAnotherAsync(row, caller, history, identities, cancellationToken);

        await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

            var now = clock.GetUtcNow();
            var released = issue.Release(now);

            history.Add(HistoryEntry.OnIssue(issue.Id, caller.Id, now, HistoryField.Claim, released.ToString(), null));
            history.Add(HistoryEntry.OnIssue(
                issue.Id, caller.Id, now, HistoryField.Status,
                ClaimIssue.SnakeCase(IssueStatus.InProgress), ClaimIssue.SnakeCase(IssueStatus.Todo)));

            await issues.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);

        return await assembler.CompleteAsync(
            await issues.FindLiveAsync(row.ProjectKey, row.Number, cancellationToken)
                ?? throw new InvalidOperationException($"Issue {row.Key} vanished under its own release."),
            cancellationToken);
    }
}
