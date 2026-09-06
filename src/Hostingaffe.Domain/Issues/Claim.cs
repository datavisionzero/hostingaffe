namespace Hostingaffe.Domain.Issues;

/// <summary>
/// The exclusive hold one identity takes on an issue: who is working
/// <em>now</em> (<c>CONTEXT.md</c>, Claim).
/// </summary>
/// <remarks>
/// <para>
/// Four facts, and the store keeps them as four columns on the issue row: who,
/// since when, when the holder last wrote, and when it lapses.
/// <see cref="ExpiresAt"/> is written rather than computed — at every extension
/// the store sets it to <see cref="ExtendedAt"/> plus the instance's deadline
/// when the holder is an agent, and to <c>null</c> when the holder is a user.
/// That null is the whole of "a user's claim never expires" (VISION 11), and it
/// is what lets the read side evaluate expiry without joining the holder's kind.
/// </para>
/// <para>
/// An expired claim is no claim, and the status falls back with it: derived on
/// read, never written by a job (VISION 11). The row still says
/// <c>in_progress</c> and still names the holder; the <c>issue_read</c> view
/// says <c>todo</c> and nobody, and the successor's claim writes the one trace
/// the expiry leaves.
/// </para>
/// </remarks>
public sealed class Claim
{
    private Claim()
    {
        // EF Core materializes through this.
    }

    public Claim(Guid holderId, DateTimeOffset claimedAt, DateTimeOffset extendedAt, DateTimeOffset? expiresAt)
    {
        if (extendedAt < claimedAt)
        {
            throw new ArgumentException("A claim is not extended before it is taken.", nameof(extendedAt));
        }

        HolderId = holderId;
        ClaimedAt = claimedAt;
        ExtendedAt = extendedAt;
        ExpiresAt = expiresAt;
    }

    public Guid HolderId { get; private init; }

    /// <summary>When the current holder took it — what "since when" shows.</summary>
    public DateTimeOffset ClaimedAt { get; private init; }

    /// <summary>
    /// The holder's last write to the issue, and only the holder's: a write by
    /// anybody else leaves this alone (VISION 11).
    /// </summary>
    public DateTimeOffset ExtendedAt { get; private init; }

    /// <summary>When it lapses, or <c>null</c> for a user's claim, which never does.</summary>
    public DateTimeOffset? ExpiresAt { get; private init; }

    public bool ExpiredAt(DateTimeOffset now) => ExpiresAt is not null && ExpiresAt <= now;

    /// <summary>
    /// A claim taken now by <paramref name="holder"/>: the expiry by the
    /// holder's kind — the deadline after the last write for an agent, never for
    /// a user (VISION 11).
    /// </summary>
    public static Claim Take(Guid holder, Identities.IdentityKind kind, DateTimeOffset at, TimeSpan agentDeadline) =>
        new(holder, at, at, ExpiryFor(kind, at, agentDeadline));

    /// <summary>The same claim, extended by a write of the holder's at <paramref name="at"/>.</summary>
    public Claim Extended(Identities.IdentityKind kind, DateTimeOffset at, TimeSpan agentDeadline) =>
        new(HolderId, ClaimedAt, at, ExpiryFor(kind, at, agentDeadline));

    private static DateTimeOffset? ExpiryFor(Identities.IdentityKind kind, DateTimeOffset at, TimeSpan agentDeadline) =>
        kind is Identities.IdentityKind.Agent ? at + agentDeadline : null;
}
