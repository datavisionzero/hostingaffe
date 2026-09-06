using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Application.Acts;

public sealed record CommentRequest(string? Body);

public sealed record AskRequest(string? Question);

public sealed record AnswerRequest(string? Answer);

public sealed record ReadQuestionRequest(int? Wait);

/// <summary>The issue a question hangs on, as far as a list of questions needs it.</summary>
public sealed record IssueRefShape(string Key, string Title);

/// <summary>A question with its issue, as <c>GET /questions</c> lists them.</summary>
public sealed record ProjectQuestionShape(
    Guid Id,
    IssueRefShape Issue,
    string Question,
    IdentityRef AskedBy,
    DateTimeOffset AskedAt,
    string? Answer,
    IdentityRef? AnsweredBy,
    DateTimeOffset? AnsweredAt);

public sealed record QuestionPage(IReadOnlyList<ProjectQuestionShape> Items, int Total, bool HasMore, string? NextCursor);

/// <summary>
/// One entry of the history (<c>docs/api.md</c>): identities rendered as
/// <see cref="IdentityRef"/> in <c>actor</c> and, for <c>assignee</c> and
/// <c>claim</c>, in the values — which is why the values are untyped.
/// </summary>
public sealed record HistoryEntryShape(
    long Id,
    IdentityRef Actor,
    DateTimeOffset At,
    string Field,
    object? OldValue,
    object? NewValue,
    string? Note);

/// <summary>
/// A comment: a note that forces nobody to act (VISION 7). On any issue, by
/// anyone, claimed or not. It moves <c>updated_at</c> and extends the holder's
/// claim when the holder wrote it.
/// </summary>
public sealed class CommentOnIssue(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<CommentShape> ExecuteAsync(string key, string? body, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        var comment = await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

            var now = clock.GetUtcNow();
            var written = Validated.Field("body", () => Comment.Write(issue.Id, caller.Id, body!, now));
            issues.Add(written);
            issue.Touch(now);
            issue.ExtendClaimIfHeldBy(caller.Id, caller.Kind, now, settings.ClaimExpiry);

            await issues.SaveAsync(cancellationToken);
            return written;
        }, cancellationToken);

        var author = await identities.FindAsync(caller.Id, cancellationToken)
            ?? throw new InvalidOperationException("The caller has no row.");

        return new CommentShape(comment.Id, IdentityRef.Of(author), comment.Body, comment.CreatedAt, comment.EditedAt);
    }
}

/// <summary>
/// Correct a comment: its author rewrites what they said (ADR 0022). Nobody
/// else may — a text somebody else rewrote is no longer what its byline says
/// was said. The correction is visible through <c>edited_at</c>, and the
/// history records that it happened without keeping the old text.
/// </summary>
public sealed class EditComment(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    IHistory history,
    ProjectScope scope,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<CommentShape> ExecuteAsync(Guid id, string? body, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var (found, row) = await Comments.LiveAsync(issues, scope, id, cancellationToken);

        if (found.AuthorId != caller.Id)
        {
            throw new Refusal(RefusalCode.Forbidden, "Only the author of a comment may rewrite it (ADR 0022).");
        }

        var comment = await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");
            var open = await issues.FindCommentAsync(id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");

            var now = clock.GetUtcNow();
            Validated.Field("body", () => { open.Rewrite(body!, now); return true; });
            history.Add(HistoryEntry.OnIssue(
                issue.Id, caller.Id, now, HistoryField.Comment, newValue: id.ToString(), note: HistoryNote.Edited));
            issue.Touch(now);
            issue.ExtendClaimIfHeldBy(caller.Id, caller.Kind, now, settings.ClaimExpiry);

            await issues.SaveAsync(cancellationToken);
            return open;
        }, cancellationToken);

        var author = await identities.FindAsync(comment.AuthorId, cancellationToken)
            ?? throw new InvalidOperationException("The author has no row.");

        return new CommentShape(comment.Id, IdentityRef.Of(author), comment.Body, comment.CreatedAt, comment.EditedAt);
    }
}

/// <summary>
/// Withdraw a comment (ADR 0022). Its author may, and so may any user on
/// anybody's — the clearing up ADR 0013 already conceded for issues, for the
/// same reason: agents leave noise and a human must be able to take it away
/// without a database client. An agent takes away only its own.
/// </summary>
public sealed class DeleteComment(
    ICallerIdentity callerIdentity,
    IIssues issues,
    IHistory history,
    ProjectScope scope,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var (found, row) = await Comments.LiveAsync(issues, scope, id, cancellationToken);

        if (found.AuthorId != caller.Id && !caller.IsUser)
        {
            throw new Refusal(RefusalCode.Forbidden, "An agent takes away only its own comments; clearing up after one is a user's (ADR 0022).");
        }

        await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");
            var open = await issues.FindCommentAsync(id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");

            var now = clock.GetUtcNow();
            issues.Remove(open);
            history.Add(HistoryEntry.OnIssue(
                issue.Id, caller.Id, now, HistoryField.Comment, oldValue: id.ToString(), note: HistoryNote.Withdrawn));
            issue.Touch(now);
            issue.ExtendClaimIfHeldBy(caller.Id, caller.Kind, now, settings.ClaimExpiry);

            await issues.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}

/// <summary>Finding a comment on a live issue the caller may see, in one place.</summary>
internal static class Comments
{
    /// <exception cref="Refusal"><c>not-found</c> where either is absent or gone.</exception>
    public static async Task<(Comment Comment, IssueRow Issue)> LiveAsync(
        IIssues issues, ProjectScope scope, Guid id, CancellationToken cancellationToken)
    {
        // A deleted issue hides its conversation with it, so the comment on one
        // is as absent as the issue — the same answer an unknown id gets.
        var comment = await issues.FindCommentAsync(id, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");
        var row = (await issues.FindLiveManyAsync([comment.IssueId], cancellationToken)).SingleOrDefault()
            ?? throw new Refusal(RefusalCode.NotFound, $"No comment {id}.");

        await scope.RequireAsync(row.ProjectId, cancellationToken);

        return (comment, row);
    }
}

/// <summary>
/// Ask: whoever cannot go on says on what (VISION 7). On any open issue. Asking
/// does not release the claim — whoever does not wait releases (VISION 10) —
/// and the asker's claim is extended when the asker holds it.
/// </summary>
public sealed class AskQuestion(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<QuestionShape> ExecuteAsync(string key, string? text, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        if (row.Closed)
        {
            throw new Refusal(RefusalCode.Transition, $"{row.Key} is closed; a question waits on an open issue.");
        }

        var question = await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {key}.");

            var now = clock.GetUtcNow();
            var asked = Validated.Field("question", () => Question.Ask(issue.ProjectId, issue.Id, text!, caller.Id, now));
            issues.Add(asked);
            issue.Touch(now);
            issue.ExtendClaimIfHeldBy(caller.Id, caller.Kind, now, settings.ClaimExpiry);

            await issues.SaveAsync(cancellationToken);
            return asked;
        }, cancellationToken);

        return await Questions.ShapeAsync(question, identities, cancellationToken);
    }
}

/// <summary>
/// Answer an open question; a second answer is <c>transition</c>. Users and
/// agents alike — the convention that an agent answers only when told to is a
/// convention (VISION 10).
/// </summary>
public sealed class AnswerQuestion(
    ICallerIdentity callerIdentity,
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    ITransactions transactions,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<QuestionShape> ExecuteAsync(Guid id, string? answer, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;

        var found = await issues.FindQuestionAsync(id, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No question {id}.");
        var row = (await issues.FindLiveManyAsync([found.IssueId], cancellationToken)).SingleOrDefault()
            ?? throw new Refusal(RefusalCode.NotFound, $"No question {id}.");
        await scope.RequireAsync(row.ProjectId, cancellationToken);

        var question = await transactions.RunAsync(async () =>
        {
            var issue = await issues.LoadForWriteAsync(row.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No issue {row.Key}.");
            var open = await issues.FindQuestionAsync(id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No question {id}.");

            var now = clock.GetUtcNow();
            if (!open.Open)
            {
                throw new Refusal(RefusalCode.Transition, "The question is answered already.");
            }

            Validated.Field("answer", () => { open.AnswerWith(answer!, caller.Id, now); return true; });
            issue.Touch(now);
            issue.ExtendClaimIfHeldBy(caller.Id, caller.Kind, now, settings.ClaimExpiry);

            await issues.SaveAsync(cancellationToken);
            return open;
        }, cancellationToken);

        return await Questions.ShapeAsync(question, identities, cancellationToken);
    }
}

/// <summary>
/// Read one question, optionally holding the request until it is answered.
/// The notification is only a pulse: every wake-up reads the question again.
/// </summary>
public sealed class ReadQuestion(
    IIdentities identities,
    IIssues issues,
    ProjectScope scope,
    IChanges changes)
{
    public async Task<QuestionShape> ExecuteAsync(
        Guid id, ReadQuestionRequest request, CancellationToken cancellationToken)
    {
        Waits.Validate(request.Wait);
        var found = await LiveAsync(id, cancellationToken);
        if (request.Wait is null || !found.Open)
        {
            return await Questions.ShapeAsync(found, identities, cancellationToken);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.Wait.Value));
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        while (true)
        {
            try
            {
                await changes.EnsureListeningAsync(found.ProjectId, waiting.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return await Questions.ShapeAsync(await LiveAsync(id, cancellationToken), identities, cancellationToken);
            }

            var changed = changes.WaitAsync(found.ProjectId, waiting.Token);
            found = await LiveAsync(id, cancellationToken);
            if (!found.Open)
            {
                await waiting.CancelAsync();
                return await Questions.ShapeAsync(found, identities, cancellationToken);
            }

            try
            {
                await changed;
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return await Questions.ShapeAsync(await LiveAsync(id, cancellationToken), identities, cancellationToken);
            }
        }
    }

    private async Task<Question> LiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var question = await issues.FindQuestionForReadAsync(id, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"No question {id}.");
        var issue = (await issues.FindLiveManyAsync([question.IssueId], cancellationToken)).SingleOrDefault();
        if (issue is null)
        {
            throw new Refusal(RefusalCode.NotFound, $"No question {id}.");
        }

        await scope.RequireAsync(issue.ProjectId, cancellationToken);

        return question;
    }
}

internal static class Waits
{
    public const int MaximumSeconds = 3600;

    /// <summary>
    /// The validator of a page, which is the hash of the page as it goes on the
    /// wire. It makes no assumption about what a change is, which is what lets
    /// the coarse wake channel of <see cref="IChanges"/> stay coarse: a caller
    /// woken by somebody else's change asks again and is answered <c>304</c>.
    /// </summary>
    public static string ETag<T>(T page) =>
        $"\"{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)))}\"";

    /// <summary>Whether an <c>If-None-Match</c> header covers this validator.</summary>
    public static bool Matches(string candidates, string tag) =>
        candidates.Split(',').Select(candidate => candidate.Trim()).Any(candidate => candidate == tag || candidate == "*");

    public static void Validate(int? wait)
    {
        if (wait is <= 0)
        {
            throw Refusal.Validation("wait", "wait is a positive number of seconds.");
        }
        if (wait is > MaximumSeconds)
        {
            throw new Refusal(
                RefusalCode.WaitTooLong,
                $"wait is at most {MaximumSeconds} seconds.",
                new Dictionary<string, object?> { ["maximum"] = MaximumSeconds });
        }
    }
}

public sealed record QuestionListRequest(string? Project, bool? Open, string? Issue, string? Search, string? Cursor, int? Limit);

/// <summary>
/// "Are there open questions?" as a list (VISION 7): across the project, oldest
/// first, open by default.
/// </summary>
public sealed class ListQuestions(IProjects projects, IIdentities identities, IIssues issues, ProjectScope scope, InstanceSettings settings)
{
    public async Task<QuestionPage> ExecuteAsync(QuestionListRequest request, CancellationToken cancellationToken)
    {
        var limit = request.Limit ?? ListIssues.DefaultLimit;
        if (limit < 1 || limit > ListIssues.MaximumLimit)
        {
            throw Refusal.Validation("limit", $"limit is 1 to {ListIssues.MaximumLimit}.");
        }

        Guid? projectId = request.Project is null ? null : (await projects.LiveAsync(request.Project, settings, cancellationToken)).Id;
        if (projectId is { } selectedProjectId)
            await scope.RequireAsync(selectedProjectId, cancellationToken);
        Guid? issueId = request.Issue is null ? null : (await issues.LiveAsync(request.Issue, settings, cancellationToken)).Id;
        var query = new QuestionQuery(await scope.ProjectIdsAsync(cancellationToken), projectId, request.Open ?? true, issueId,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim());

        var after = request.Cursor is null ? null : Questions.Decode(request.Cursor, query);
        var page = await issues.ListQuestionsAsync(query, after, limit, cancellationToken);

        var people = await identities.FindManyAsync(
            page.Items.SelectMany(r => new[] { (Guid?)r.Question.AskedBy, r.Question.AnsweredBy }).OfType<Guid>().Distinct(),
            cancellationToken);

        return new QuestionPage(
            [
                .. page.Items.Select(r => new ProjectQuestionShape(
                    r.Question.Id,
                    new IssueRefShape(r.IssueKey, r.IssueTitle),
                    r.Question.Text,
                    IdentityRef.Of(people[r.Question.AskedBy]),
                    r.Question.AskedAt,
                    r.Question.Answer,
                    r.Question.AnsweredBy is { } by && people.TryGetValue(by, out var answerer) ? IdentityRef.Of(answerer) : null,
                    r.Question.AnsweredAt)),
            ],
            page.Total,
            page.HasMore,
            page.HasMore ? Questions.Encode(query, page.Items[^1].Question) : null);
    }
}

/// <summary>The history of an issue: who, when, which field, from what to what, oldest first.</summary>
public sealed class ReadHistory(IIdentities identities, IIssues issues, ProjectScope scope, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var row = await issues.LiveAsync(key, settings, cancellationToken);
        await scope.RequireAsync(row.ProjectId, cancellationToken);
        var entries = await history.ListAsync(row.Id, cancellationToken);

        var people = await identities.FindManyAsync(
            entries.Select(e => (Guid?)e.ActorId)
                .Concat(entries.Where(IsIdentityValued).SelectMany(e => new[] { Parse(e.OldValue), Parse(e.NewValue) }))
                .OfType<Guid>()
                .Distinct(),
            cancellationToken);

        return
        [
            .. entries.Select(e => new HistoryEntryShape(
                e.Id,
                IdentityRef.Of(people[e.ActorId]),
                e.At,
                e.Field,
                IsIdentityValued(e) ? Render(people, e.OldValue) : e.OldValue,
                IsIdentityValued(e) ? Render(people, e.NewValue) : e.NewValue,
                e.Note)),
        ];
    }

    private static bool IsIdentityValued(HistoryEntry e) => e.Field is HistoryField.Assignee or HistoryField.Claim;

    private static Guid? Parse(string? value) => Guid.TryParse(value, out var id) ? id : null;

    private static object? Render(IReadOnlyDictionary<Guid, Identity> people, string? value) =>
        Parse(value) is { } id && people.TryGetValue(id, out var identity) ? IdentityRef.Of(identity) : value;
}

internal static class Questions
{
    private sealed record Payload(string F, DateTimeOffset T, Guid I);

    public static async Task<QuestionShape> ShapeAsync(Question question, IIdentities identities, CancellationToken cancellationToken)
    {
        var people = await identities.FindManyAsync(new[] { (Guid?)question.AskedBy, question.AnsweredBy }.OfType<Guid>(), cancellationToken);

        return new QuestionShape(
            question.Id,
            question.Text,
            IdentityRef.Of(people[question.AskedBy]),
            question.AskedAt,
            question.Answer,
            question.AnsweredBy is { } by ? IdentityRef.Of(people[by]) : null,
            question.AnsweredAt);
    }

    public static string Encode(QuestionQuery query, Question last) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Payload(Fingerprint(query), last.AskedAt, last.Id)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static QuestionPosition Decode(string cursor, QuestionQuery query)
    {
        Payload? payload;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(base64));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            payload = null;
        }

        return payload is null || payload.F != Fingerprint(query)
            ? throw new Refusal(RefusalCode.CursorInvalid, "The cursor is not one this server issued for these filters.")
            : new QuestionPosition(payload.T, payload.I);
    }

    private static string Fingerprint(QuestionQuery query) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(query))))[..16];
}
