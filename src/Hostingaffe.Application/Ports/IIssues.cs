using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// An issue as it is read — through <c>issue_read</c>, with the project key
/// beside it so that the key can be spelled without another lookup. The two
/// deletion columns are set only on the one read that sees deleted rows.
/// </summary>
public sealed record IssueRow
{
    // Init-only members rather than a constructor, so that EF Core can see
    // through the projection: a `Where` on a member bound by an initializer
    // translates, one on a constructor argument does not.
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectKey { get; init; }

    public required int Number { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public string? Result { get; init; }

    public required IssueStatus Status { get; init; }

    public required bool Ready { get; init; }

    public required Priority Priority { get; init; }

    public Guid? AssigneeId { get; init; }

    public Guid? EpicId { get; init; }

    /// <summary>
    /// The number behind the <c>E</c> of the epic's key, joined in beside the
    /// id because <c>sort=epic</c> orders by the key a reader sees and not by a
    /// Guid that means nothing to anybody.
    /// </summary>
    public int? EpicNumber { get; init; }

    public Guid? ParentId { get; init; }

    public Guid? ClaimedBy { get; init; }

    public DateTimeOffset? ClaimedAt { get; init; }

    public DateTimeOffset? ClaimExpiresAt { get; init; }

    public required Guid AuthorId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public Guid? DeletedBy { get; init; }

    public string Key => IssueKey.Of(ProjectKey, Number);

    /// <summary>The epic's key, or <c>null</c> where the issue hangs under none.</summary>
    public string? EpicKey => EpicNumber is { } number ? Domain.Projects.EpicKey.Of(ProjectKey, number) : null;

    public bool Closed => Status is IssueStatus.Done or IssueStatus.Canceled;
}

/// <summary>A blocker edge with the issue at the far end, as the view sees it; a deleted far end is not returned at all.</summary>
public sealed record EdgeRow(Guid NearId, IssueRow Far);

/// <summary>A label on an issue.</summary>
public sealed record IssueLabelRow(Guid IssueId, Label Label);

public enum IssueSort
{
    Updated,
    Created,
    Priority,

    /// <summary>
    /// The epic as the first sort key, so that a group opens exactly once and
    /// not again on the next page: epic key ascending with the issues under no
    /// epic last, then priority descending, then the number.
    /// </summary>
    Epic,
}

public enum SortOrder
{
    Asc,
    Desc,
}

/// <summary>
/// The filters of <c>GET /issues</c> after names have become ids: what the
/// store can translate.
/// </summary>
/// <param name="Claimed"><c>null</c>: any; <c>true</c>/<c>false</c>: whether held; an id: held by that identity.</param>
public sealed record IssueQuery(
    IReadOnlyCollection<Guid> AllowedProjectIds,
    Guid? ProjectId,
    IReadOnlyList<IssueStatus> Statuses,
    bool? Ready,
    Priority? PriorityMin,
    Priority? PriorityMax,
    IReadOnlyList<string> LabelNames,
    Guid? EpicId,
    bool EpicNone,
    Guid? AssigneeId,
    bool AssigneeNone,
    bool? ClaimedAtAll,
    Guid? ClaimedBy,
    Guid? AuthorId,
    bool? Blocked,
    bool? HasOpenQuestion,
    string? Search,
    bool Deleted);

/// <summary>
/// Where a page ended: the sort key, the number and the id of its last item.
/// The number breaks a tie in the sort key before the id does — issues created
/// in one act share a timestamp, and the id's order within a millisecond is
/// random — the way the index <c>issue_next</c> is ordered.
/// </summary>
/// <param name="ProjectKey">Under <c>sort=epic</c> the first half of the epic key; empty otherwise.</param>
/// <param name="EpicNumber">Under <c>sort=epic</c> the second half, <c>null</c> where the last item hung under no epic.</param>
public sealed record IssuePosition(
    DateTimeOffset? Time, Priority? Priority, int Number, Guid Id, string ProjectKey = "", int? EpicNumber = null);

public sealed record IssuePageRows(IReadOnlyList<IssueRow> Items, int Total, bool HasMore);

/// <summary>Why an issue is in the human's work list, in the order VISION 10 gives the groups.</summary>
public enum NeedsYouBecause
{
    Question,
    Review,
    Unready,
    Stuck,
}

/// <summary>Where a needs-you page ended: group first, then priority, age and identity.</summary>
public sealed record NeedsYouPosition(NeedsYouBecause Because, Priority Priority, DateTimeOffset CreatedAt, int Number, Guid Id);

/// <summary>An issue selected for needs-you, before its slim shape is assembled.</summary>
public sealed record NeedsYouRow(Guid Id, NeedsYouBecause Because);

/// <param name="Agents">Agents that could pick work up at all — a fact about the instance, not about any row.</param>
public sealed record NeedsYouPageRows(IReadOnlyList<NeedsYouRow> Items, int Total, bool HasMore, int Agents);

/// <summary>
/// What <c>next</c> asks for: the project, the caller the eight conditions are
/// evaluated for, and the filters of <c>docs/api.md</c> with names already
/// checked against the project.
/// </summary>
/// <param name="RequireReady">Where triage is required, or where `ready` was asked for.</param>
/// <param name="RepoLabel">The `.hostingaffe` file's label: issues carrying it or no label of the `repo` group.</param>
public sealed record NextQuery(
    Guid ProjectId,
    Guid CallerId,
    bool RequireReady,
    Guid? EpicId,
    IReadOnlyList<string> Labels,
    string? RepoLabel);

/// <summary>
/// Why the rest was not handed out (VISION 10): seven independent counts over
/// the open issues the filters match — an issue can count under several.
/// </summary>
public sealed record Reasons(
    int Blocked,
    int WaitingForAnswer,
    int InProgress,
    int InReview,
    int Parked,
    int NotReady,
    int AssignedElsewhere,
    int ParentGated);

/// <summary>
/// The issue rows and everything that hangs on them. Reads go through the view;
/// writes take the row and lock it.
/// </summary>
public interface IIssues
{
    Task<IssueRow?> FindLiveAsync(string projectKey, int number, CancellationToken cancellationToken);

    /// <summary>The one read that sees deleted rows: for the `deleted` answer, and for restore.</summary>
    Task<IssueRow?> FindDeletedAsync(string projectKey, int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueRow>> FindLiveManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<IssuePageRows> ListAsync(
        IssueQuery query, IssueSort sort, SortOrder order, IssuePosition? after, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// The workable issues for the caller, in the order <c>next</c> hands them
    /// out: priority, then an epic nobody else is working in, then age. With
    /// <paramref name="lockOne"/>, inside a transaction, the first one is
    /// locked <c>for update skip locked</c> — so that two callers at once get
    /// two different issues and neither waits.
    /// </summary>
    Task<IReadOnlyList<Guid>> NextWorkableAsync(NextQuery query, int limit, bool lockOne, CancellationToken cancellationToken);

    Task<int> CountWorkableAsync(NextQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// What only a human can resolve: questions, review, unready under triage,
    /// and blocker chains that reach a dead end (VISION 10).
    /// </summary>
    Task<NeedsYouPageRows> NeedsYouAsync(
        Guid projectId, bool triageRequired, NeedsYouPosition? after, int limit, CancellationToken cancellationToken);

    Task<Reasons> ReasonsAsync(NextQuery query, CancellationToken cancellationToken);

    /// <summary>The row itself, tracked and locked <c>for update</c> for the rest of the transaction.</summary>
    Task<Issue?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The same, for a deleted row: what restore works on.</summary>
    Task<Issue?> LoadDeletedForWriteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueLabelRow>> LabelsOfAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);

    /// <summary>What blocks each of <paramref name="issueIds"/>: the near end is the blocked issue.</summary>
    Task<IReadOnlyList<EdgeRow>> BlockersOfAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);

    /// <summary>What each of <paramref name="issueIds"/> blocks: the near end is the blocker.</summary>
    Task<IReadOnlyList<EdgeRow>> BlockedByEachAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> OpenQuestionCountsAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> OpenSubIssueCountsAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<IssueRow>> SubIssuesOfAsync(Guid issueId, CancellationToken cancellationToken);

    /// <summary>Includes deleted rows so a parent can never be purged from under a child.</summary>
    Task<bool> HasSubIssuesAsync(Guid issueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Comment>> CommentsOfAsync(Guid issueId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Question>> QuestionsOfAsync(Guid issueId, CancellationToken cancellationToken);

    void Add(Issue issue);

    void Add(Comment comment);

    /// <summary>One comment by id, tracked, whatever the state of its issue.</summary>
    Task<Comment?> FindCommentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Takes a comment away for good; the history keeps that it was (ADR 0022).</summary>
    void Remove(Comment comment);

    void Add(Question question);

    Task<Question?> FindQuestionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A fresh question snapshot for a waiting read; never served from the change tracker.</summary>
    Task<Question?> FindQuestionForReadAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Questions across the project, oldest first, on live issues only.</summary>
    Task<QuestionPageRows> ListQuestionsAsync(QuestionQuery query, QuestionPosition? after, int limit, CancellationToken cancellationToken);

    void Attach(IssueLabel attachment);

    Task DetachAsync(Guid issueId, Guid labelId, CancellationToken cancellationToken);

    void Add(Blocker blocker);

    Task RemoveBlockerAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    Task<bool> HasBlockerAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    /// <summary>
    /// The chain from <paramref name="blockerId"/> backwards through what
    /// blocks it, bounded to a hundred steps, as far as
    /// <paramref name="blockedId"/> — the cycle the edge would close — or
    /// <c>null</c> when it does not reach it (<c>docs/storage.md</c>, Blockers).
    /// Asked with the edge already written, inside the transaction.
    /// </summary>
    Task<IReadOnlyList<Guid>?> CycleThroughAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>A question with the issue it hangs on, as <c>GET /questions</c> lists them.</summary>
public sealed record QuestionRow(Question Question, string IssueKey, string IssueTitle);

/// <param name="Open"><c>true</c>: unanswered only; <c>false</c>: answered only; <c>null</c>: both.</param>
public sealed record QuestionQuery(IReadOnlyCollection<Guid> AllowedProjectIds, Guid? ProjectId, bool? Open, Guid? IssueId, string? Search);

/// <summary>Where a page of questions ended: asked when, and the id.</summary>
public sealed record QuestionPosition(DateTimeOffset AskedAt, Guid Id);

public sealed record QuestionPageRows(IReadOnlyList<QuestionRow> Items, int Total, bool HasMore);

/// <summary>The history rows: appended, never edited (<c>CONTEXT.md</c>).</summary>
public interface IHistory
{
    void Add(Domain.History.HistoryEntry entry);

    /// <summary>Every entry of an issue, oldest first. Not paginated.</summary>
    Task<IReadOnlyList<Domain.History.HistoryEntry>> ListAsync(Guid issueId, CancellationToken cancellationToken);

    /// <summary>The newest entry of <paramref name="field"/> on the issue, or none.</summary>
    Task<Domain.History.HistoryEntry?> LastAsync(Guid issueId, string field, CancellationToken cancellationToken);
}

/// <summary>
/// One transaction around several port calls — the bulk create, the guarded
/// <c>PATCH</c>, a label change under the issue's lock. The stores share the
/// unit of work, so what is added inside is committed together or not at all.
/// </summary>
public interface ITransactions
{
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>What an epic has come to (VISION 7): counted from its issues at read time, deleted ones excluded.</summary>
public sealed record Progress(int Total, int Closed, int Done, int Canceled);

/// <param name="Closed"><c>null</c>: open and closed alike.</param>
public sealed record EpicQuery(IReadOnlyCollection<Guid> AllowedProjectIds, Guid? ProjectId, bool? Closed, IReadOnlyList<string> LabelNames);

/// <summary>Where a page of epics ended: created when, the number, the id — newest first.</summary>
public sealed record EpicPosition(DateTimeOffset CreatedAt, int Number, Guid Id);

public sealed record EpicPageRows(IReadOnlyList<Domain.Epics.Epic> Items, int Total, bool HasMore);

public sealed record EpicLabelRow(Guid EpicId, Label Label);

/// <summary>The epic rows.</summary>
public interface IEpics
{
    Task<Domain.Epics.Epic?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>By key, live only — what an issue may be attached to.</summary>
    Task<Domain.Epics.Epic?> FindLiveAsync(Guid projectId, int number, CancellationToken cancellationToken);

    /// <summary>By key, deleted or not — for the `deleted` answer and for restore.</summary>
    Task<Domain.Epics.Epic?> FindAnyAsync(Guid projectId, int number, CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Epics.Epic>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Live epics, newest first.</summary>
    Task<EpicPageRows> ListAsync(EpicQuery query, EpicPosition? after, int limit, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Domain.Epics.Epic?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Progress per epic, over the issues that are not deleted.</summary>
    Task<IReadOnlyDictionary<Guid, Progress>> ProgressAsync(IReadOnlyCollection<Guid> epicIds, CancellationToken cancellationToken);

    /// <summary>How many issues reference the epic, deleted ones included — what refuses a delete.</summary>
    Task<int> ReferencingIssuesAsync(Guid epicId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EpicLabelRow>> LabelsOfAsync(IReadOnlyCollection<Guid> epicIds, CancellationToken cancellationToken);

    void Add(Domain.Epics.Epic epic);

    void Attach(Domain.Epics.EpicLabel attachment);

    Task DetachAsync(Guid epicId, Guid labelId, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
