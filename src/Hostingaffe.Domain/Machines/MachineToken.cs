using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Domain.Machines;

/// <summary>
/// The key a machine holds so that it can hand in a report for itself, and
/// nothing else (<c>CONTEXT.md</c>, Identity;
/// <a href="../../../docs/adr/0016-a-machine-token-posts-one-report-and-reads-nothing.md">ADR 0016</a>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not an identity.</strong> No user, no agent, no role: it is
/// absent from <c>ha me</c> and from every history row, and what it delivers is
/// attributed to the machine, which is not a who. That is why this type carries
/// no identity and sits beside the machine instead of beside
/// <see cref="Token"/>.
/// </para>
/// <para>
/// It is stored the way every other token is — the SHA-256 and the first eight
/// characters (<see cref="TokenSecret"/>) — because nothing about tokens is
/// invented twice. One per machine, live; a revoked one stays as a row so that
/// "there was one, and who took it back when" has an answer, and the partial
/// unique index is what keeps exactly one of them live.
/// </para>
/// <para>
/// <see cref="LastUsedAt"/> moves on every delivery. It is one write per report
/// per machine, and the only way to see afterwards whether a token is still in
/// use.
/// </para>
/// </remarks>
public sealed class MachineToken
{
    private MachineToken()
    {
        // EF Core materializes through this; every other route goes through Issue.
    }

    private MachineToken(
        Guid id, Guid machineId, string prefix, byte[] secretHash, Guid issuedBy, DateTimeOffset issuedAt)
    {
        Id = id;
        MachineId = machineId;
        Prefix = prefix;
        SecretHash = secretHash;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
    }

    public Guid Id { get; private init; }

    /// <summary>The one machine it may report for.</summary>
    public Guid MachineId { get; private init; }

    public string Prefix { get; private init; } = null!;

    public byte[] SecretHash { get; private init; } = null!;

    /// <summary>The person who issued it. An agent never does (ADR 0016).</summary>
    public Guid IssuedBy { get; private init; }

    public DateTimeOffset IssuedAt { get; private init; }

    /// <summary>When a report last arrived under it; nothing where none ever has.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public bool Revoked => RevokedAt is not null;

    /// <exception cref="ArgumentException">
    /// <paramref name="secret"/> is not a secret a token is made of.
    /// </exception>
    public static MachineToken Issue(Guid machineId, string secret, Guid issuedBy, DateTimeOffset issuedAt) =>
        new(
            Guid.CreateVersion7(),
            machineId,
            TokenSecret.PrefixOf(secret),
            TokenSecret.HashOf(secret),
            issuedBy,
            issuedAt);

    /// <summary>
    /// Takes effect immediately and cannot be undone; revoking a revoked token
    /// changes nothing and keeps the first answer to "who took it back when".
    /// </summary>
    public void Revoke(Guid by, DateTimeOffset at)
    {
        if (Revoked)
        {
            return;
        }

        RevokedAt = at;
        RevokedBy = by;
    }

    /// <summary>Moves the mark a report leaves; a revoked token leaves none.</summary>
    public void Used(DateTimeOffset at)
    {
        if (!Revoked)
        {
            LastUsedAt = at;
        }
    }
}
