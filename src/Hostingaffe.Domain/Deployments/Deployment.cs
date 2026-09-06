using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain.Deployments;

/// <summary>
/// The record that an installation changed version (<c>CONTEXT.md</c>,
/// Deployment; VISION 7). It is the history of the installation and the source
/// of its current version — and it is the reason the installation carries no
/// version field of its own.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no status.</strong> A deployment is recorded when it is
/// done. A rollback is a deployment to the previous version with a note that
/// says so; an attempt that changed nothing is a note on the installation or a
/// ticket, not a row here. That keeps the list honest: every row is a version
/// that really ran.
/// </para>
/// <para>
/// <see cref="At"/> is when the version went live and may be set, so that
/// history can be backfilled. <see cref="CreatedBy"/> — <c>by</c> on the wire —
/// is who <em>recorded</em> it, which for a backfilled deployment is not
/// necessarily who deployed.
/// </para>
/// <para>
/// <strong>The correction rule is fixed</strong>, because an agent will record
/// the wrong thing: <see cref="Ref"/>, <see cref="Ticket"/>, <see cref="Note"/>
/// and <see cref="At"/> change and the history says so; <see cref="Version"/>
/// and the installation do not, because they are what the record <em>is</em>. A
/// deployment with the wrong version is deleted and recorded again.
/// </para>
/// </remarks>
public sealed class Deployment
{
    public const int VersionMaxLength = 200;

    /// <summary>What an image reference with a digest, or a git SHA, fits in.</summary>
    public const int RefMaxLength = 500;

    /// <summary>What a planaffe key fits in.</summary>
    public const int TicketMaxLength = 64;

    private Deployment()
    {
        // EF Core materializes through this; every other route goes through Record.
    }

    private Deployment(
        Guid id, Guid installationId, int number, string version, DateTimeOffset at, Guid by, DateTimeOffset recordedAt)
    {
        Id = id;
        InstallationId = installationId;
        Number = number;
        Version = version;
        At = at;
        Note = string.Empty;
        CreatedBy = by;
        CreatedAt = recordedAt;
        UpdatedBy = by;
        UpdatedAt = recordedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>Whose version changed. It does not move: that is what the record is.</summary>
    public Guid InstallationId { get; private init; }

    /// <summary>
    /// What the instance numbered it, counted from one per installation. A
    /// deployment has no key; this is what the API and the CLI address it by.
    /// </summary>
    public int Number { get; private init; }

    /// <summary>What ran afterwards — <c>2.11.4</c>, <c>main-20260905</c>. It does not change.</summary>
    public string Version { get; private init; } = null!;

    /// <summary>The exact thing that was deployed: an image reference with a digest, a git SHA, a tag.</summary>
    public string? Ref { get; private set; }

    /// <summary>When the version went live. Settable, so that history can be backfilled.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>A planaffe key like <c>LOG-42</c>: a reference to the other product, not a word of this model.</summary>
    public string? Ticket { get; private set; }

    /// <summary>Markdown: why, what was checked, what went wrong.</summary>
    public string Note { get; private set; } = null!;

    /// <summary>Who recorded it — <c>by</c> on the wire.</summary>
    public Guid CreatedBy { get; private init; }

    /// <summary>When it was recorded, which for a backfilled deployment is not <see cref="At"/>.</summary>
    public DateTimeOffset CreatedAt { get; private init; }

    public Guid UpdatedBy { get; private set; }

    /// <summary>The version a guarded write is compared against.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool Deleted => DeletedAt is not null;

    /// <exception cref="ArgumentException">The version does not hold.</exception>
    public static Deployment Record(
        Guid installationId, int number, string? version, DateTimeOffset at, Guid by, DateTimeOffset recordedAt) =>
        new(Guid.CreateVersion7(), installationId, number, NormalizeVersion(version), at, by, recordedAt);

    /// <summary>
    /// The four fields that may be corrected, and what actually changed.
    /// </summary>
    /// <exception cref="ArgumentException">A value does not hold; the message names the field.</exception>
    public IReadOnlyList<FieldChange> Apply(DeploymentEdit edit, Guid by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var changes = new List<FieldChange>();

        Fields.Text("ref", edit.Ref, Ref, value => Ref = value, value => Fields.Line(value, RefMaxLength, "A ref"), changes);
        Fields.Text("ticket", edit.Ticket, Ticket, value => Ticket = value, value => Fields.Line(value, TicketMaxLength, "A ticket"), changes);

        if (edit.At is { } moment && moment != At)
        {
            changes.Add(new FieldChange("at", Fields.Stamp(At), Fields.Stamp(moment)));
            At = moment;
        }

        // A note records that it changed, never how: it is Markdown, and the
        // text itself is one read away.
        if (edit.Note is not null && edit.Note != Note)
        {
            changes.Add(new FieldChange("note", null, null));
            Note = edit.Note;
        }

        if (changes.Count > 0)
        {
            UpdatedBy = by;
            UpdatedAt = now;
        }

        return changes;
    }

    /// <summary>Soft, with the grace period of everything else (ADR 0013).</summary>
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

    /// <exception cref="ArgumentException"><paramref name="version"/> is blank, spans lines, or is too long.</exception>
    public static string NormalizeVersion(string? version)
    {
        var trimmed = version?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? throw new ArgumentException("A deployment is a version that ran; it says which.", nameof(version))
            : Fields.Line(trimmed, VersionMaxLength, "A version");
    }
}

/// <summary>
/// What a caller wants a deployment to say afterwards. Only the four fields a
/// correction may touch are here — <c>version</c> and the installation are what
/// the record is, and a deployment with the wrong version is deleted and
/// recorded again (VISION 7).
/// </summary>
public sealed record DeploymentEdit
{
    public string? Ref { get; init; }

    public DateTimeOffset? At { get; init; }

    public string? Ticket { get; init; }

    public string? Note { get; init; }
}
