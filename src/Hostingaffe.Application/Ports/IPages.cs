using Hostingaffe.Domain.Pages;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The page rows (<c>docs/storage.md</c>, Pages). A page is found by its slug,
/// because that is its address (ADR 0021).
/// </summary>
/// <remarks>
/// There is no paged list here and no cursor. The wiki is flat, its pages are
/// few, and the list is slim — slug, title, who touched it last —
/// for the same reason ADR 0012 keeps a list slim: the body is what would make
/// it expensive, and the body is not in it.
/// </remarks>
public interface IPages
{
    /// <summary>By slug, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<Page?> FindAnyAsync(string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Every live page, by slug; <paramref name="search"/> filters by the words
    /// in the title and the body.
    /// </summary>
    Task<IReadOnlyList<Page>> ListAsync(string? search, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Page?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    void Add(Page page);

    Task SaveAsync(CancellationToken cancellationToken);
}
