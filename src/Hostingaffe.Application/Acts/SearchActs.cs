using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>One hit as the contract serves it.</summary>
public sealed record SearchHitShape(
    string Kind,
    string Key,
    string Name,
    int? Number,
    AnchorShape? Owner,
    string Where);

/// <summary>
/// "Where was that again" — a port number, an address, a name — asked once over
/// every field, every Markdown body and every file (VISION 5, 6.3).
/// </summary>
/// <remarks>
/// <para>
/// Not paginated, because it is not a list of one thing and there is no order a
/// cursor could walk. It is <strong>capped</strong> instead: a word that occurs
/// in every file would otherwise answer with the whole record, which is not an
/// answer.
/// </para>
/// <para>
/// The words are matched the way Postgres splits text, and a whole path is one
/// of those words. A <em>piece</em> of one is not, so a query that is a single
/// word with a slash or a dot in it is looked for as a fragment as well — over
/// the same surfaces, through the trigram indexes, and never instead of the
/// words (ADR 0012).
/// </para>
/// </remarks>
public sealed class Search(ISearch search)
{
    /// <summary>What a caller gets without asking for a number.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The most anyone gets, whatever they ask for.</summary>
    public const int MaximumLimit = 200;

    public async Task<IReadOnlyList<SearchHitShape>> ExecuteAsync(
        string? query, int? limit, CancellationToken cancellationToken)
    {
        var words = (query ?? string.Empty).Trim();

        if (words.Length == 0)
        {
            throw Refusal.Validation("q", "A search needs something to search for.");
        }

        if (limit is <= 0)
        {
            throw Refusal.Validation("limit", "A limit is a count, and a count is at least one.");
        }

        var capped = Math.Min(limit ?? DefaultLimit, MaximumLimit);

        return
        [
            .. (await search.FindAsync(words, capped, cancellationToken)).Select(hit => new SearchHitShape(
                hit.Kind,
                hit.Key,
                hit.Name,
                hit.Number,
                hit.Owner is { } owner ? new AnchorShape(owner.Kind, owner.Key) : null,
                hit.Where)),
        ];
    }
}
