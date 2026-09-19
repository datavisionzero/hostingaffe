using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Application.Acts;

/// <summary>One field an act changed, as the contract serves it.</summary>
public sealed record FieldChangeShape(string Field, string? OldValue, string? NewValue);

/// <summary>
/// One event of the history read across every subject: what happened, to what,
/// on which machine, and who did it.
/// </summary>
/// <param name="Cursor">What to send as <c>before</c> to go on after this event.</param>
public sealed record HistoryEventShape(
    DateTimeOffset At,
    IdentityRef Actor,
    string SubjectKind,
    string? Subject,
    int? Number,
    string? Machine,
    IReadOnlyList<FieldChangeShape> Changes,
    string? Note,
    string Cursor);

/// <summary>
/// "What happened lately" (<c>docs/api.md</c>, The history): the history of
/// every subject in one reading, newest first, with the deployments mixed in.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is stored for this and nothing changes in the record. The rows one
/// act wrote are folded into one event, because an act that touched three
/// fields is one thing that happened; and a deployment is read beside them
/// rather than turned into a history row, which it is not and does not become
/// (<c>CONTEXT.md</c>, History).
/// </para>
/// <para>
/// A subject that has been deleted keeps its events. The history survives the
/// deletion of what it describes and the purge with it (VISION 7), and a
/// reading of what happened that dropped the deletions would answer the
/// opposite of what it was asked: the last thing that happened to a machine
/// somebody removed is that somebody removed it.
/// </para>
/// <para>
/// There is no notion of an interesting event here. Every act is one line and
/// the reader decides; a filter that hid the dull ones would be a rule nobody
/// can see, and the one thing worse than a long list is a short list that is
/// quietly wrong.
/// </para>
/// </remarks>
public sealed class ReadHistory(
    IHistory history, IMachines machines, IIdentities identities, InstanceSettings settings)
{
    /// <summary>What a caller gets without asking for a number.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most anyone gets, whatever they ask for.</summary>
    public const int MaximumLimit = 200;

    public async Task<IReadOnlyList<HistoryEventShape>> ExecuteAsync(
        string? machine, string? kind, string? before, int? limit, CancellationToken cancellationToken)
    {
        if (limit is <= 0)
        {
            throw Refusal.Validation("limit", "A limit is a count, and a count is at least one.");
        }

        var subject = Validated.Field("kind", () => Spelling.Read<HistorySubject>(kind, "kind"));
        var key = string.IsNullOrWhiteSpace(machine)
            ? null
            : (await machines.LiveAsync(machine.Trim(), settings, cancellationToken)).Key;

        var events = await history.ReadAsync(
            key, subject, Read(before), Math.Min(limit ?? DefaultLimit, MaximumLimit), cancellationToken);

        var people = await identities.FindManyAsync(
            events.Select(one => one.ActorId).Distinct(), cancellationToken);

        return
        [
            .. events.Select(one => new HistoryEventShape(
                one.At,
                IdentityRef.Of(people[one.ActorId]),
                one.SubjectKind,
                one.Subject,
                one.Number,
                one.Machine,
                [.. one.Changes.Select(change => new FieldChangeShape(change.Field, change.OldValue, change.NewValue))],
                one.Note,
                Write(one.Cursor))),
        ];
    }

    /// <summary>
    /// The cursor as a caller carries it: opaque, because what it holds is this
    /// reading's sort key and not an address anybody may build one from.
    /// </summary>
    private static string Write(HistoryCursor cursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{cursor.At.ToString("o", CultureInfo.InvariantCulture)}|{cursor.Source}|{cursor.Ident}"));

    /// <exception cref="Refusal"><c>cursor-invalid</c> where it is not one this reading wrote.</exception>
    private static HistoryCursor? Read(string? before)
    {
        if (string.IsNullOrWhiteSpace(before))
        {
            return null;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(before.Trim())).Split('|');

            if (parts.Length == 3 && parts[1].Length > 0 && parts[2].Length > 0)
            {
                return new HistoryCursor(
                    DateTimeOffset.ParseExact(parts[0], "o", CultureInfo.InvariantCulture),
                    parts[1],
                    parts[2]);
            }
        }
        catch (Exception malformed) when (malformed is FormatException or ArgumentException or DecoderFallbackException)
        {
            throw Invalid();
        }

        throw Invalid();
    }

    private static Refusal Invalid() =>
        new(RefusalCode.CursorInvalid, "A cursor is the one a previous page handed out, unchanged.");
}
