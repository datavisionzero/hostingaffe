using Hostingaffe.Domain;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// One hit: what was found, where it lives, and which of the surfaces the
/// instance searches it was found on.
/// </summary>
/// <param name="Kind">The kind of record — <c>machine</c>, <c>software</c>, <c>installation</c>, <c>deployment</c>, <c>file</c> or <c>page</c>.</param>
/// <param name="Key">Its address: a key, a page's slug, a file's path, or the installation a deployment lives under.</param>
/// <param name="Name">What it is called — a name, a title, a version. Empty where the address is the whole of it.</param>
/// <param name="Number">The deployment's number. Nothing else has one.</param>
/// <param name="Directory">Where a machine's file lies on the machine. Nothing else has one, and an installation's file has the installation's own.</param>
/// <param name="Owner">What a file belongs to or a page hangs on, and nothing for the rest.</param>
/// <param name="Where">Which surface matched: <c>fields</c>, <c>description</c>, <c>ports</c>, <c>title</c>, <c>body</c>, <c>path</c> or <c>content</c>.</param>
public sealed record SearchHit(
    string Kind,
    string Key,
    string Name,
    int? Number,
    string? Directory,
    Anchor? Owner,
    string Where);

/// <summary>
/// The one composed read of the record (<c>docs/storage.md</c>, Searching):
/// "where was that again", asked once over every field, every Markdown body and
/// every file.
/// </summary>
/// <remarks>
/// Postgres and nothing beside it — no second index to operate, which is part
/// of the promise that an instance starts from a Compose file (VISION 12). The
/// words go through the stored <c>tsvector</c> columns; a path, which Postgres
/// makes one word of, goes through the trigram indexes beside them (ADR 0012).
/// </remarks>
public interface ISearch
{
    /// <summary>
    /// Every live row the words match, capped at <paramref name="limit"/>, in
    /// the order the kinds are listed in and then by address. A hit in a
    /// deleted row is not a hit.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> FindAsync(string query, int limit, CancellationToken cancellationToken);
}
