using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Application.Acts;

/// <summary>The claim as a list shows it: who, since when, and when it lapses (<c>null</c> for a user's).</summary>
public sealed record ClaimShape(IdentityRef Holder, DateTimeOffset Since, DateTimeOffset? ExpiresAt);

/// <summary>A blocker in the slim issue: the key and whether it is still open. A hidden one is <c>null</c> key (cut three).</summary>
public sealed record BlockerRefShape(string? Key, bool Open);

/// <summary>A blocker, or a blocked issue, in the complete issue.</summary>
/// <summary>
/// A blocker or a blocked issue as the complete issue names it.
/// <paramref name="Result"/> is the outcome VISION 15.5 asks for and stands on
/// <c>blocked_by</c> only: what a predecessor decided is what the work here
/// starts from, and a successor's outcome does not exist yet. It is
/// <c>null</c> where the blocker has recorded none, and on every link the
/// caller may not see into.
/// </summary>
public sealed record BlockerLinkShape(string? Key, string? Title, IssueStatus? Status, bool Open, string? Result = null);

/// <summary>The slim issue every list returns (ADR 0012, <c>docs/api.md</c>).</summary>
public sealed record IssueSummaryShape(
    string Key,
    string Project,
    string Title,
    IssueStatus Status,
    bool Ready,
    Priority Priority,
    IReadOnlyList<string> Labels,
    string? Epic,
    string? Parent,
    string? Release,
    IdentityRef? Assignee,
    ClaimShape? Claim,
    IReadOnlyList<BlockerRefShape> BlockedBy,
    int OpenQuestions,
    int OpenBlockers,
    int OpenSubIssues,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IdentityRef? DeletedBy);

public sealed record EpicRefShape(string Key, string Title, string Description, Domain.Epics.EpicStatus Status);

/// <param name="EditedAt">When its author last corrected it, or nothing while it is as written (ADR 0022).</param>
public sealed record CommentShape(Guid Id, IdentityRef Author, string Body, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);

public sealed record QuestionShape(
    Guid Id,
    string Question,
    IdentityRef AskedBy,
    DateTimeOffset AskedAt,
    string? Answer,
    IdentityRef? AnsweredBy,
    DateTimeOffset? AnsweredAt);

/// <summary>
/// The page a project designates as its instructions (<c>CONTEXT.md</c>,
/// Instructions), as the context package carries it: the document itself, and
/// the address it can be changed at.
/// </summary>
public sealed record InstructionsShape(string Slug, string Title, string Body);

/// <summary>
/// The project as the complete issue carries it: the switches, the labels with
/// their descriptions, and the instructions every agent is handed with every
/// ticket.
/// </summary>
public sealed record ProjectContextShape(
    string Key,
    string Name,
    bool TriageRequired,
    bool ReviewRequired,
    IReadOnlyList<LabelShape> Labels,
    InstructionsShape? Instructions);

/// <summary>
/// The complete issue — the context package of VISION 15.5: the ticket, its
/// comments and questions, the epic's description, the project's labels with
/// their descriptions and the project's instructions, in one read.
/// </summary>
public sealed record IssueShape(
    string Key,
    string Project,
    string Title,
    string Description,
    string? Result,
    IssueStatus Status,
    bool Ready,
    Priority Priority,
    IReadOnlyList<LabelShape> Labels,
    EpicRefShape? Epic,
    IssueRefShape? Parent,
    string? Release,
    IReadOnlyList<IssueRefShape> SubIssues,
    IdentityRef? Assignee,
    ClaimShape? Claim,
    IdentityRef Author,
    IReadOnlyList<BlockerLinkShape> BlockedBy,
    IReadOnlyList<BlockerLinkShape> Blocks,
    int OpenQuestions,
    int OpenBlockers,
    int OpenSubIssues,
    IReadOnlyList<CommentShape> Comments,
    IReadOnlyList<QuestionShape> Questions,
    ProjectContextShape ProjectContext,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>A page of slim issues (<c>docs/api.md</c>, Pagination).</summary>
public sealed record IssuePage(IReadOnlyList<IssueSummaryShape> Items, int Total, bool HasMore, string? NextCursor);

/// <summary>What <c>POST /issues</c> answers: the complete issues, in the order given.</summary>
public sealed record CreatedIssues(IReadOnlyList<IssueShape> Items);
