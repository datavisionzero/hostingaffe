namespace Hostingaffe.Domain.Reports;

/// <summary>
/// What a machine said about itself at a moment (<c>CONTEXT.md</c>, Report;
/// VISION 7). A sample beside the record, never a field of it.
/// </summary>
/// <remarks>
/// <para>
/// A report belongs to exactly one machine, has no key — the instance numbers
/// it per machine, as it does a deployment — and is <strong>never
/// edited</strong>. There is no <c>Apply</c> here and no history row anywhere:
/// the history is who changed the record, and a cron reporting every quarter of
/// an hour has changed nothing
/// (<a href="../../../docs/adr/0015-a-machine-reports-and-the-record-stays-written.md">ADR 0015</a>).
/// </para>
/// <para>
/// <see cref="CollectedAt"/> is the host's clock, kept as it came and trusted
/// for nothing. <see cref="ReceivedAt"/> is the instance's, and it is what the
/// order and a machine's <c>last_seen</c> are read from. A
/// <see cref="CollectedAt"/> far in the future is stored rather than refused:
/// denying a machine with a wrong clock its sign of life would be the worse
/// answer.
/// </para>
/// <para>
/// A report carries no <c>deleted_at</c>. It is reachable only through its
/// machine, so a deleted machine's reports are invisible for as long as the
/// machine is, a restore brings them back with it, and the purge takes them
/// with the row — without stamping thousands of samples one at a time
/// (<c>docs/storage.md</c>, Reports).
/// </para>
/// </remarks>
public sealed class Report
{
    /// <summary>What the version of the <c>ha</c> that collected it fits in.</summary>
    public const int AgentMaxLength = 64;

    private Report()
    {
        // EF Core materializes through this; every other route goes through Record.
    }

    private Report(
        Guid id,
        Guid machineId,
        int number,
        DateTimeOffset collectedAt,
        DateTimeOffset receivedAt,
        string? agent,
        ReportBody body)
    {
        Id = id;
        MachineId = machineId;
        Number = number;
        CollectedAt = collectedAt;
        ReceivedAt = receivedAt;
        Agent = agent;
        Body = body;
    }

    public Guid Id { get; private init; }

    /// <summary>Whose sample it is. It does not move.</summary>
    public Guid MachineId { get; private init; }

    /// <summary>
    /// What the instance numbered it, counted from one per machine. A report
    /// has no key; this is what the API and the CLI address it by.
    /// </summary>
    public int Number { get; private init; }

    /// <summary>The host's clock, as it came.</summary>
    public DateTimeOffset CollectedAt { get; private init; }

    /// <summary>The instance's clock: the order, and <c>last_seen</c>.</summary>
    public DateTimeOffset ReceivedAt { get; private init; }

    /// <summary>The version of the <c>ha</c> that collected it, where it said.</summary>
    public string? Agent { get; private init; }

    /// <summary>The closed set of sections, and what was missing from them.</summary>
    public ReportBody Body { get; private init; } = null!;

    /// <exception cref="ArgumentException"><paramref name="number"/> is not a number a report is given.</exception>
    public static Report Record(
        Guid machineId,
        int number,
        DateTimeOffset collectedAt,
        DateTimeOffset receivedAt,
        string? agent,
        ReportBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        var version = agent?.Trim();

        return new Report(
            Guid.CreateVersion7(),
            machineId,
            number,
            collectedAt,
            receivedAt,
            string.IsNullOrEmpty(version)
                ? null
                : Fields.Line(version, AgentMaxLength, "An agent version"),
            body);
    }
}
