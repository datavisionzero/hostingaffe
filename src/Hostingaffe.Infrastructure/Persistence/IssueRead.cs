using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// One row of <c>issue_read</c>: the issue with the two derived rules applied
/// (<c>docs/storage.md</c>, What is derived on read). A deleted issue is not
/// here; an expired claim is <c>null</c> here and the status is <c>todo</c>.
/// Every read of an issue goes through this type, and writes never do.
/// </summary>
/// <remarks>
/// Keyless and view-mapped, so the migrations leave it alone: the view is SQL in
/// the migration that created the table. The claim is three columns rather than
/// the owned <see cref="Claim"/>, because the view does not carry the holder's
/// last write — nothing that reads needs it.
/// </remarks>
public sealed class IssueRead
{
    public Guid Id { get; init; }

    public Guid ProjectId { get; init; }

    public int Number { get; init; }

    public string Title { get; init; } = null!;

    public string Description { get; init; } = null!;

    public string? Result { get; init; }

    public IssueStatus Status { get; init; }

    public bool Ready { get; init; }

    public Priority Priority { get; init; }

    public Guid? AssigneeId { get; init; }

    public Guid? EpicId { get; init; }

    public Guid? ParentId { get; init; }

    public Guid? ClaimedBy { get; init; }

    public DateTimeOffset? ClaimedAt { get; init; }

    public DateTimeOffset? ClaimExpiresAt { get; init; }

    public Guid AuthorId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }
}
