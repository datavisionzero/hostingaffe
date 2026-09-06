using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain.Pages;

/// <summary>
/// A Markdown document addressed by its slug: the instance's flat wiki
/// (<c>CONTEXT.md</c>, Page; VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// The slug it is reached by may be renamed: the old one leads nowhere
/// afterwards, and the rename stands in the history (ADR 0021).
/// </para>
/// <para>
/// <see cref="UpdatedAt"/> is the version, so that a page inherits the guarded
/// write of <c>docs/api.md</c> ("Guarding a write") rather than
/// carrying a mechanism of its own. Every edit moves it and names who made it,
/// because a wiki's list is read for who touched what last.
/// </para>
/// <para>
/// Uniqueness of the slug is the store's and the database's, not this type's:
/// it needs the other pages. A deleted page keeps its slug until the purge
/// takes it, so that restoring one never lands on a name somebody else has
/// taken (ADR 0013).
/// </para>
/// </remarks>
public sealed class Page
{
    public const int TitleMaxLength = 200;

    private Page()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Page(Guid id, string slug, string title, string body, PageKind kind, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Slug = slug;
        Title = title;
        Body = body;
        Kind = kind;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = createdBy;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>The address, unique in the instance and renameable (ADR 0021).</summary>
    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    /// <summary>Markdown, rendered in the browser and never as HTML (ADR 0007).</summary>
    public string Body { get; private set; } = null!;

    /// <summary>What it is to be read as, and nothing beyond that.</summary>
    public PageKind Kind { get; private set; }

    /// <summary>Set when the page hangs on a machine, and then the other one is not.</summary>
    public Guid? MachineId { get; private set; }

    /// <summary>Set when the page hangs on an installation, and then the other one is not.</summary>
    public Guid? InstallationId { get; private set; }

    /// <summary>
    /// Whether it hangs on anything at all. A page that does not belongs to the
    /// instance as a whole, which is a state the model has on purpose (VISION 7).
    /// </summary>
    public bool Attached => MachineId is not null || InstallationId is not null;

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Who made the last change — what the list shows without reading the history.</summary>
    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    public static Page Create(
        string slug, string title, string? body, PageKind kind, Guid createdBy, DateTimeOffset createdAt) =>
        new(
            Guid.CreateVersion7(),
            Domain.Pages.Slug.Normalize(slug),
            NormalizeTitle(title),
            body ?? string.Empty,
            Enum.IsDefined(kind) ? kind : throw new ArgumentException("Not a kind.", nameof(kind)),
            createdBy,
            createdAt);

    /// <summary>What the page is to be read as. Answers the change, or nothing where there is none.</summary>
    public FieldChange? Reclassify(PageKind kind, Guid by, DateTimeOffset at)
    {
        if (kind == Kind)
        {
            return null;
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("Not a kind.", nameof(kind));
        }

        var change = new FieldChange("kind", Spelling.Of(Kind), Spelling.Of(kind));
        Kind = kind;
        Touch(by, at);

        return change;
    }

    /// <summary>
    /// What the page hangs on, or nothing — and then it belongs to the instance
    /// as a whole. Answers the change, or nothing where there is none.
    /// </summary>
    public FieldChange? AttachTo(Anchor? anchor, Guid by, DateTimeOffset at)
    {
        var was = MachineId ?? InstallationId;

        if (anchor?.Id == was && (anchor is null) == (was is null))
        {
            return null;
        }

        MachineId = anchor?.Kind is AnchorKind.Machine ? anchor.Id : null;
        InstallationId = anchor?.Kind is AnchorKind.Installation ? anchor.Id : null;

        Touch(by, at);

        // The old anchor is a row the Domain does not resolve, so the entry
        // carries what it became — the same way a machine's host does.
        return new FieldChange("attached_to", null, anchor?.ToString());
    }

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

    /// <summary>Every edit moves the version and names who made it.</summary>
    private void Touch(Guid by, DateTimeOffset at)
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
