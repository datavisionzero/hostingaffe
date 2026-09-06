namespace Hostingaffe.Domain.Pages;

/// <summary>
/// A Markdown document in a project, addressed by its slug: the project's flat
/// wiki, and the place a plan lives until tickets are cut from it
/// (<c>CONTEXT.md</c>, Page; VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// It is the one object in the product not reached through a key (ADR 0021),
/// and the slug it is reached by may be renamed: the old one leads nowhere
/// afterwards, and the rename stands in the history.
/// </para>
/// <para>
/// <see cref="UpdatedAt"/> is the version, as at the issue and the epic, so
/// that a page inherits the guarded write of <c>docs/api.md</c>
/// ("Concurrency on text fields") rather than carrying a mechanism of its own.
/// Every edit moves it and names who made it, because a wiki's list is read
/// for who touched what last.
/// </para>
/// <para>
/// Uniqueness of the slug is the store's and the database's, not this type's:
/// it needs the project's other pages. A deleted page keeps its slug until the
/// purge takes it, so that restoring one never lands on a name somebody else
/// has taken (ADR 0013).
/// </para>
/// </remarks>
public sealed class Page
{
    public const int TitleMaxLength = 200;

    private Page()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Page(Guid id, Guid projectId, string slug, string title, string body, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Slug = slug;
        Title = title;
        Body = body;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid ProjectId { get; private init; }

    /// <summary>The address, unique within the project and renameable (ADR 0021).</summary>
    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    /// <summary>Markdown, rendered in the browser and never as HTML (ADR 0007).</summary>
    public string Body { get; private set; } = null!;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Who made the last change — what the list shows without reading the history.</summary>
    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    public static Page Create(Guid projectId, string slug, string title, string? body, Guid createdBy, DateTimeOffset createdAt) =>
        new(
            Guid.CreateVersion7(),
            projectId,
            Domain.Pages.Slug.Normalize(slug),
            NormalizeTitle(title),
            body ?? string.Empty,
            createdBy,
            createdAt);

    /// <summary>The address changes and the old one leads nowhere; nothing forwards (ADR 0021).</summary>
    public void Rename(string slug, Guid by, DateTimeOffset at)
    {
        Slug = Domain.Pages.Slug.Normalize(slug);
        Touch(by, at);
    }

    public void Retitle(string title, Guid by, DateTimeOffset at)
    {
        Title = NormalizeTitle(title);
        Touch(by, at);
    }

    /// <summary>The document itself; <c>null</c> empties it.</summary>
    public void Rewrite(string? body, Guid by, DateTimeOffset at)
    {
        Body = body ?? string.Empty;
        Touch(by, at);
    }

    /// <summary>A change to the labels moves the version too, so that a guarded write sees it.</summary>
    public void Touch(Guid by, DateTimeOffset at)
    {
        UpdatedBy = by;
        UpdatedAt = at;
    }

    /// <summary>Soft, with the grace period of everything else; the slug stays spent until the purge (ADR 0013).</summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        DeletedAt = at;
        DeletedBy = by;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <exception cref="ArgumentException">
    /// <paramref name="title"/> is blank, spans lines, or is longer than
    /// <see cref="TitleMaxLength"/>.
    /// </exception>
    public static string NormalizeTitle(string title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A page has a title.", nameof(title));
        }

        return trimmed.Length > TitleMaxLength || trimmed.Contains('\n')
            ? throw new ArgumentException(
                $"A page title is one line of at most {TitleMaxLength} characters.", nameof(title))
            : trimmed;
    }
}
