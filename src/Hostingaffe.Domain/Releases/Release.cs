namespace Hostingaffe.Domain.Releases;

/// <summary>A project's record of what shipped, not a plan of what should ship (VISION 7).</summary>
public sealed class Release
{
    public const int NameMaxLength = 100;

    private Release() { }

    private Release(Guid id, Guid projectId, DateTimeOffset at)
    {
        Id = id;
        ProjectId = projectId;
        Description = string.Empty;
        Status = ReleaseStatus.Open;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public Guid Id { get; private init; }
    public Guid ProjectId { get; private init; }
    public string? Name { get; private set; }
    public string Description { get; private set; } = null!;
    public ReleaseStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Release Open(Guid projectId, DateTimeOffset at) => new(Guid.CreateVersion7(), projectId, at);

    public void Describe(string? description, DateTimeOffset at)
    {
        Description = description ?? string.Empty;
        UpdatedAt = at;
    }

    public void Publish(string name, string? description, Guid by, DateTimeOffset at)
    {
        if (Status is ReleaseStatus.Published)
        {
            throw new InvalidOperationException("A published release is frozen.");
        }

        Name = NormalizeName(name);
        if (description is not null)
        {
            Description = description;
        }
        Status = ReleaseStatus.Published;
        PublishedAt = at;
        PublishedBy = by;
        UpdatedAt = at;
    }

    /// <summary>
    /// Correct the name of the publication, which is a different thing from
    /// rewriting the record: only the newest publication may be renamed, and
    /// the act that calls this decides that (VISION 7).
    /// </summary>
    public void Rename(string name, DateTimeOffset at)
    {
        if (Status is not ReleaseStatus.Published)
        {
            throw new InvalidOperationException("Only a published release carries a name.");
        }

        Name = NormalizeName(name);
        UpdatedAt = at;
    }

    /// <summary>
    /// Take the publication back: this release is the open one again. The
    /// correction of a fumble, and only ever of the newest publication — which
    /// the act that calls this establishes.
    /// </summary>
    public void Retract(DateTimeOffset at)
    {
        if (Status is not ReleaseStatus.Published)
        {
            throw new InvalidOperationException("The release is not published.");
        }

        Name = null;
        Status = ReleaseStatus.Open;
        PublishedAt = null;
        PublishedBy = null;
        UpdatedAt = at;
    }

    public static string NormalizeName(string name)
    {
        var value = name?.Trim();
        if (string.IsNullOrEmpty(value) || value.Contains('\n') || value.Length > NameMaxLength)
        {
            throw new ArgumentException($"A release name is one line of at most {NameMaxLength} characters.", nameof(name));
        }
        if (value.Equals("unreleased", StringComparison.OrdinalIgnoreCase) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("unreleased and none are reserved release names.", nameof(name));
        }
        return value;
    }
}
