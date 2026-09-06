namespace Hostingaffe.Domain.Epics;

/// <summary>
/// A theme several issues hang under, and a description that is the shared
/// context for whoever works on them — a bracket, not a unit of work: no
/// assignee, no priority, no claim (<c>CONTEXT.md</c>, Epic).
/// </summary>
/// <remarks>
/// <para>
/// Progress is not a column. It is counted from the issues at read time,
/// excluding deleted ones, split into done and canceled (VISION 7).
/// </para>
/// <para>
/// The description is a living document several agents edit, which is why the
/// API guards it with an update against the known <see cref="UpdatedAt"/>
/// (<c>docs/api.md</c>). An epic with issues cannot be deleted; attaching an
/// issue to a closed one reopens it. Both rules are the store's, because both
/// need the issues.
/// </para>
/// </remarks>
public sealed class Epic
{
    public const int TitleMaxLength = 200;

    private Epic()
    {
        // EF Core materializes through this; every other route goes through Create.
    }

    private Epic(Guid id, Guid projectId, int number, string title, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        ProjectId = projectId;
        Number = number;
        Title = title;
        Description = string.Empty;
        Status = EpicStatus.Open;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    public Guid ProjectId { get; private init; }

    /// <summary>
    /// The number behind the <c>E</c> in <c>PLAN-E3</c>, drawn from the project's
    /// epic counter. The key itself is the project key and this, joined at read
    /// time; it is not stored.
    /// </summary>
    public int Number { get; private init; }

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public EpicStatus Status { get; private set; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Closed => Status is EpicStatus.Closed;

    public bool Deleted => DeletedAt is not null;

    public static Epic Create(Guid projectId, int number, string title, Guid createdBy, DateTimeOffset createdAt) =>
        number < 1
            ? throw new ArgumentOutOfRangeException(nameof(number), "An epic number is drawn from one upwards.")
            : new(Guid.CreateVersion7(), projectId, number, NormalizeTitle(title), createdBy, createdAt);

    public void Retitle(string title, DateTimeOffset at)
    {
        Title = NormalizeTitle(title);
        UpdatedAt = at;
    }

    /// <summary>The living document (VISION 7); <c>null</c> clears it to nothing.</summary>
    public void Describe(string? description, DateTimeOffset at)
    {
        Description = description ?? string.Empty;
        UpdatedAt = at;
    }

    /// <summary>Closed, whatever is still open: the bracket gates nothing (VISION 7).</summary>
    /// <exception cref="Refusal"><c>transition</c> when it is closed already.</exception>
    public void Close(DateTimeOffset at)
    {
        if (Closed)
        {
            throw new Refusal(RefusalCode.Transition, "The epic is closed already.");
        }

        Status = EpicStatus.Closed;
        ClosedAt = at;
        UpdatedAt = at;
    }

    /// <summary>Soft, and only while no issue references it — the store asks that first (ADR 0013).</summary>
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

    /// <summary>A change to the labels moves the version, so that a guarded write sees it.</summary>
    public void Touch(DateTimeOffset at) => UpdatedAt = at;

    /// <summary>
    /// Attaching an issue to a closed epic reopens it, in the same transaction
    /// (VISION 7); reopening an open one changes nothing.
    /// </summary>
    public void Reopen(DateTimeOffset at)
    {
        if (!Closed)
        {
            return;
        }

        Status = EpicStatus.Open;
        ClosedAt = null;
        UpdatedAt = at;
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
            throw new ArgumentException("An epic has a title.", nameof(title));
        }

        return trimmed.Length > TitleMaxLength || trimmed.Contains('\n')
            ? throw new ArgumentException(
                $"An epic title is one line of at most {TitleMaxLength} characters.", nameof(title))
            : trimmed;
    }
}
