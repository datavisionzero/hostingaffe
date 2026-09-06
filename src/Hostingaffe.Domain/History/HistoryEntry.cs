namespace Hostingaffe.Domain.History;

/// <summary>
/// One row of the history (<c>CONTEXT.md</c>): who, when, which field, from
/// what to what. Written by the instance, never edited.
/// </summary>
/// <remarks>
/// <para>
/// A row names its subject — <see cref="Subject"/> and <see cref="SubjectId"/>
/// — rather than being a column of it, so that a second kind of subject is a
/// value and not a migration of the table. The pair carries no foreign key for
/// the same reason, which is also what lets a row outlive what it describes:
/// VISION 7 wants the history of a deleted machine to still say that it existed
/// and when it was deleted.
/// </para>
/// <para>
/// What "outlives" means for the purge is settled where deleting is settled;
/// what is settled here is that nothing in the schema forces a row to die with
/// its subject.
/// </para>
/// </remarks>
public sealed class HistoryEntry
{
    private HistoryEntry()
    {
        // EF Core materializes through this; every other route goes through On.
    }

    private HistoryEntry(
        HistorySubject subject,
        Guid subjectId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue,
        string? newValue,
        string? note)
    {
        Subject = subject;
        SubjectId = subjectId;
        ActorId = actorId;
        At = at;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        Note = note;
    }

    /// <summary>Assigned by the database, in the order the rows were written.</summary>
    public long Id { get; private init; }

    /// <summary>Which kind of thing this row is about.</summary>
    public HistorySubject Subject { get; private init; }

    /// <summary>Which one of them, by row id.</summary>
    public Guid SubjectId { get; private init; }

    public Guid ActorId { get; private init; }

    public DateTimeOffset At { get; private init; }

    /// <summary>One of <see cref="HistoryField"/>, or a field of the subject spelled as the API spells it.</summary>
    public string Field { get; private init; } = null!;

    public string? OldValue { get; private init; }

    public string? NewValue { get; private init; }

    /// <summary>
    /// What the two values cannot carry, for the changes that need a word
    /// beside them.
    /// </summary>
    public string? Note { get; private init; }

    public static HistoryEntry On(
        HistorySubject subject,
        Guid subjectId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        new(subject, subjectId, actorId, at, Named(field), oldValue, newValue, note);

    public static HistoryEntry OnPage(
        Guid pageId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        On(HistorySubject.Page, pageId, actorId, at, field, oldValue, newValue, note);

    public static HistoryEntry OnMachine(
        Guid machineId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        On(HistorySubject.Machine, machineId, actorId, at, field, oldValue, newValue, note);

    public static HistoryEntry OnSoftware(
        Guid softwareId,
        Guid actorId,
        DateTimeOffset at,
        string field,
        string? oldValue = null,
        string? newValue = null,
        string? note = null) =>
        On(HistorySubject.Software, softwareId, actorId, at, field, oldValue, newValue, note);

    private static string Named(string field) =>
        string.IsNullOrWhiteSpace(field)
            ? throw new ArgumentException("A history entry names the field that changed.", nameof(field))
            : field;
}
