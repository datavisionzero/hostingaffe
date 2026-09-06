namespace Hostingaffe.Domain.History;

/// <summary>
/// One row of the history (<c>CONTEXT.md</c>): who, when, which field, from
/// what to what. Written by the instance, never edited and never deleted — it
/// dies only with its subject (ADR 0013).
/// </summary>
/// <remarks>
/// An issue's history, an epic's and a page's live in one table because they
/// are one concept and the two smaller ones are tiny; every row points at
/// exactly one of the three, and the table's check constraint holds that too.
/// </remarks>
public sealed class HistoryEntry
{
    private HistoryEntry()
    {
        // EF Core materializes through this; every other route goes through OnIssue, OnEpic or OnPage.
    }

    private HistoryEntry(
        Guid? issueId,
        Guid? epicId,
        Guid? pageId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue,
        string? newValue,
        string? note)
    {
        IssueId = issueId;
        EpicId = epicId;
        PageId = pageId;
        ActorId = actorId;
        At = at;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        Note = note;
    }

    /// <summary>Assigned by the database, in the order the rows were written.</summary>
    public long Id { get; private init; }

    public Guid? IssueId { get; private init; }

    public Guid? EpicId { get; private init; }

    public Guid? PageId { get; private init; }

    public Guid ActorId { get; private init; }

    public DateTimeOffset At { get; private init; }

    /// <summary>One of <see cref="HistoryField"/>.</summary>
    public string Field { get; private init; } = null!;

    public string? OldValue { get; private init; }

    public string? NewValue { get; private init; }

    /// <summary>One of <see cref="HistoryNote"/>, or nothing.</summary>
    public string? Note { get; private init; }

    public static HistoryEntry OnIssue(
        Guid issueId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        new(issueId, null, null, actorId, at, Named(field), oldValue, newValue, note);

    public static HistoryEntry OnEpic(
        Guid epicId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        new(null, epicId, null, actorId, at, Named(field), oldValue, newValue, note);

    public static HistoryEntry OnPage(
        Guid pageId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        new(null, null, pageId, actorId, at, Named(field), oldValue, newValue, note);

    private static string Named(string field) =>
        string.IsNullOrWhiteSpace(field)
            ? throw new ArgumentException("A history entry names the field that changed.", nameof(field))
            : field;
}
