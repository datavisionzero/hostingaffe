namespace Hostingaffe.Domain.History;

/// <summary>
/// One row of the history (<c>CONTEXT.md</c>): who, when, which field, from
/// what to what. Written by the instance, never edited and never deleted — it
/// dies only with its subject (ADR 0013).
/// </summary>
/// <remarks>
/// The page is the only subject there is at present. The table is written to
/// carry more than one — a row names its subject rather than being a column of
/// it — because the entities this product is about get their history the same
/// way, and a mechanism that has to be rebuilt to take a second subject is one
/// that was built for the first by accident.
/// </remarks>
public sealed class HistoryEntry
{
    private HistoryEntry()
    {
        // EF Core materializes through this; every other route goes through OnPage.
    }

    private HistoryEntry(
        Guid pageId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue,
        string? newValue,
        string? note)
    {
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

    public Guid PageId { get; private init; }

    public Guid ActorId { get; private init; }

    public DateTimeOffset At { get; private init; }

    /// <summary>One of <see cref="HistoryField"/>.</summary>
    public string Field { get; private init; } = null!;

    public string? OldValue { get; private init; }

    public string? NewValue { get; private init; }

    /// <summary>
    /// What the two values cannot carry, for the changes that need a word
    /// beside them. Nothing writes one yet; the column is here because the
    /// history is a mechanism and not a table this product filled in.
    /// </summary>
    public string? Note { get; private init; }

    public static HistoryEntry OnPage(
        Guid pageId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        new(pageId, actorId, at, Named(field), oldValue, newValue, note);

    private static string Named(string field) =>
        string.IsNullOrWhiteSpace(field)
            ? throw new ArgumentException("A history entry names the field that changed.", nameof(field))
            : field;
}
