namespace Hostingaffe.Domain.Projects;

/// <summary>
/// The bracket every piece of content belongs to, carrying the project key that
/// prefixes everything in it (<c>CONTEXT.md</c>, Project). It is not a
/// repository.
/// </summary>
/// <remarks>
/// The key is typed by a person and never changes; everything in the project is
/// prefixed by it.
/// </remarks>
public sealed class Project
{
    public const int NameMaxLength = 100;

    private Project()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Project(Guid id, string key, string name, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        Key = key;
        Name = name;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>Never changed after creation (ADR 0015).</summary>
    public string Key { get; private init; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>
    /// The one page every agent is handed with every ticket
    /// (<c>CONTEXT.md</c>, Instructions; VISION 15.3), or <c>null</c> where the
    /// project designates none. One page and not a flag on each of them: three
    /// marked pages would be three pages of context on every ticket, and the
    /// context budget is the resource the whole idea is about (VISION 6.1).
    /// </summary>
    /// <remarks>
    /// It is the page's id and not its slug, so that renaming the address the
    /// wiki reaches it by (ADR 0021) leaves the designation where it was. The
    /// designation follows the page: a deleted one delivers nothing until it is
    /// restored, and the purge clears this column with the row.
    /// </remarks>
    public Guid? InstructionsPageId { get; private set; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    public static Project Create(string key, string name, Guid createdBy, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), ProjectKey.Normalize(key), NormalizeName(name), createdBy, createdAt);

    /// <summary>
    /// Point the project at the page every agent is handed with its ticket, or
    /// at none. Which page exists, and that it is one of this project's, is the
    /// act's to establish; this type only holds the pointer.
    /// </summary>
    public void Instruct(Guid? pageId, DateTimeOffset at)
    {
        InstructionsPageId = pageId;
        UpdatedAt = at;
    }

    public void Rename(string name, DateTimeOffset at)
    {
        Name = NormalizeName(name);
        UpdatedAt = at;
    }

    /// <summary>
    /// The soft delete of ADR 0013, with everything in the project: invisible,
    /// restorable for the grace period, gone after the purge. The key stays
    /// taken meanwhile.
    /// </summary>
    public void Delete(Guid by, DateTimeOffset at)
    {
        if (Deleted)
        {
            return;
        }

        DeletedAt = at;
        DeletedBy = by;
    }

    /// <summary>Back, with everything in it, into whatever state it was.</summary>
    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank or longer than <see cref="NameMaxLength"/>.
    /// </exception>
    public static string NormalizeName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("A project has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"A project name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
