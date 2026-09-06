namespace Hostingaffe.Domain.Issues;

/// <summary>
/// An issue another issue waits for (<c>CONTEXT.md</c>, Blocker): one directed
/// edge, read from both ends, which may cross projects and dissolves on its own
/// when the blocker closes.
/// </summary>
/// <remarks>
/// A cycle is refused on write by the store, with a bounded recursive walk;
/// what this type refuses is the one-edge cycle, which the table refuses too.
/// </remarks>
public sealed class Blocker
{
    private Blocker()
    {
        // EF Core materializes through this; every other route goes through Between.
    }

    private Blocker(Guid blockerId, Guid blockedId, Guid createdBy, DateTimeOffset createdAt)
    {
        BlockerId = blockerId;
        BlockedId = blockedId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    /// <summary>The issue that blocks.</summary>
    public Guid BlockerId { get; private init; }

    /// <summary>The issue that waits.</summary>
    public Guid BlockedId { get; private init; }

    public Guid CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <exception cref="ArgumentException">An issue cannot block itself.</exception>
    public static Blocker Between(Guid blockerId, Guid blockedId, Guid createdBy, DateTimeOffset createdAt) =>
        blockerId == blockedId
            ? throw new ArgumentException("An issue cannot block itself.", nameof(blockedId))
            : new(blockerId, blockedId, createdBy, createdAt);
}
