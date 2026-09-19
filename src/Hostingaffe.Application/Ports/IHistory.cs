using Hostingaffe.Domain.History;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// Where a walk through the history stands: the sort key of the event that was
/// handed out last.
/// </summary>
/// <remarks>
/// Three parts rather than a timestamp, because two events can carry the same
/// <c>at</c> — a deployment may be backfilled onto a moment an act already
/// wrote — and a walk that cut on time alone would drop whatever shared the
/// second it stopped in. <see cref="Source"/> says which of the two sides the
/// event came from and <see cref="Ident"/> which row of it, so that the three
/// together are unique and the order over them is total.
/// </remarks>
public sealed record HistoryCursor(DateTimeOffset At, string Source, string Ident);

/// <summary>
/// One event of the history read across every subject (<c>docs/api.md</c>,
/// The history): the fields one act changed on one thing, or a deployment.
/// </summary>
/// <remarks>
/// <para>
/// A row of the history is one field, and an act that touched three wrote
/// three rows carrying the same actor, the same moment and the same note. Here
/// they are one event with three changes, because that is what happened: the
/// grouping is a reading and nothing in the table moves.
/// </para>
/// <para>
/// A deployment is no history row and does not become one (<c>CONTEXT.md</c>,
/// History). It is read beside them and mixed in on the way out, because the
/// question this read answers — what happened lately — is the one question a
/// deployment is the best answer to.
/// </para>
/// </remarks>
/// <param name="SubjectKind">The kind of thing it happened to, spelled as the contract spells it.</param>
/// <param name="Subject">Its address — a key, a path, a slug, or the installation a deployment lives under. Nothing where the purge has taken the row.</param>
/// <param name="Number">The deployment's number, and nothing on anything else.</param>
/// <param name="Machine">The key of the machine it belongs to, or nothing where it belongs to none.</param>
public sealed record HistoryEvent(
    DateTimeOffset At,
    HistoryCursor Cursor,
    string SubjectKind,
    string? Subject,
    int? Number,
    string? Machine,
    Guid ActorId,
    IReadOnlyList<FieldChange> Changes,
    string? Note);

/// <summary>The history rows: appended, never edited (<c>CONTEXT.md</c>).</summary>
public interface IHistory
{
    void Add(HistoryEntry entry);

    /// <summary>Every entry about one subject, oldest first. Not paginated.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistorySubject subject, Guid subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// The history across every subject, newest first, with the deployments
    /// mixed in: at most <paramref name="limit"/> events, starting after
    /// <paramref name="before"/> where a walk is under way.
    /// </summary>
    /// <param name="machine">The key of the machine to stay on, or nothing for the whole instance.</param>
    /// <param name="kind">The one kind of subject to keep, or nothing for all of them.</param>
    Task<IReadOnlyList<HistoryEvent>> ReadAsync(
        string? machine,
        HistorySubject? kind,
        HistoryCursor? before,
        int limit,
        CancellationToken cancellationToken);
}
