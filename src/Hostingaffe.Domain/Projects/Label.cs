namespace Hostingaffe.Domain.Projects;

/// <summary>
/// A free tag defined per project, optionally carrying a one-line description
/// of what it means there, and the only extensibility the product offers
/// (<c>CONTEXT.md</c>, Label).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Group"/> is the label group: a name several labels share, within
/// which only one applies at a time. The rule that a group admits one label is
/// held by the write path under the row lock of whatever carries the label —
/// an issue or an epic — not by this type and not by the database
/// (<c>docs/storage.md</c>, Labels).
/// </para>
/// <para>
/// Deleting a label is a soft delete with the same grace period as an issue,
/// and its attachments stay: invisible while deleted, back when restored.
/// </para>
/// </remarks>
public sealed class Label
{
    public const int DescriptionMaxLength = 500;

    private Label()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Label(
        Guid id,
        Guid projectId,
        string name,
        string? group,
        string? description,
        DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Group = group;
        Description = description;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid ProjectId { get; private init; }

    /// <summary>Unique within the project.</summary>
    public string Name { get; private set; } = null!;

    /// <summary>The label group, or none.</summary>
    public string? Group { get; private set; }

    /// <summary>One line of Markdown saying what the label means in this project.</summary>
    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool Deleted => DeletedAt is not null;

    public static Label Create(
        Guid projectId,
        string name,
        string? group,
        string? description,
        DateTimeOffset createdAt) =>
        new(
            Guid.CreateVersion7(),
            projectId,
            LabelName.Normalize(name),
            group is null ? null : LabelName.Normalize(group, nameof(group)),
            NormalizeDescription(description),
            createdAt);

    /// <summary>The group every new project starts with (VISION 8), and what replaces an issue type.</summary>
    public const string KindGroup = "kind";

    /// <summary>
    /// The group of the label a `.hostingaffe` file names where one project spans
    /// several repositories (VISION 13). Not created with the project; whoever
    /// needs it creates it.
    /// </summary>
    public const string RepoGroup = "repo";

    /// <summary>
    /// The three labels of the <c>kind</c> group, each with the one line that
    /// says what it means — because for an agent the project's label set is
    /// the schema, and a schema without a word of documentation gets guessed at.
    /// </summary>
    public static IReadOnlyList<Label> Kind(Guid projectId, DateTimeOffset createdAt) =>
    [
        Create(projectId, "bug", KindGroup, "Something that should work and does not.", createdAt),
        Create(projectId, "feature", KindGroup, "Something the product should do and does not yet.", createdAt),
        Create(projectId, "chore", KindGroup, "Work that keeps the project healthy and changes no behaviour: dependencies, tooling, cleanup.", createdAt),
    ];

    public void Rename(string name) => Name = LabelName.Normalize(name);

    /// <summary>
    /// Moves the label into <paramref name="group"/>, or out of every group with
    /// <c>null</c>. Whether an issue would then carry two of the group is the
    /// store's question, asked before this is called.
    /// </summary>
    public void Regroup(string? group) => Group = group is null ? null : LabelName.Normalize(group, nameof(group));

    public void Describe(string? description) => Description = NormalizeDescription(description);

    /// <summary>Soft: invisible on every issue that carries it, the attachments kept for a restore.</summary>
    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    public void Restore() => DeletedAt = null;

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > DescriptionMaxLength || trimmed.Contains('\n')
            ? throw new ArgumentException(
                $"A label description is one line of at most {DescriptionMaxLength} characters.",
                nameof(description))
            : trimmed;
    }
}
