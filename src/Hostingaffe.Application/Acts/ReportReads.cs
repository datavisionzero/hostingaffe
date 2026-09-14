using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// One line of the series: enough for "on the 3rd the disk went from 60 to 91
/// per cent", and not a whole body per row.
/// </summary>
/// <remarks>
/// Everything here is counted from the body on read, and none of it is stored
/// beside the body it is counted from — two numbers for one fact are one
/// disagreement away from being useless (ADR 0014, ADR 0015).
/// </remarks>
public sealed record ReportSummaryShape(
    int Number,
    DateTimeOffset ReceivedAt,
    DateTimeOffset CollectedAt,
    int? ContainersRunning,
    int? ContainersTotal,
    int? DiskPercent,
    double? Load1,
    bool? RebootRequired);

/// <summary>A page of a machine's reports, newest first, and how many there are in all.</summary>
public sealed record ReportPageShape(int Total, IReadOnlyList<ReportSummaryShape> Reports);

/// <summary>The complete report: the sections as they arrived, and what was missing from them.</summary>
public sealed record ReportShape(
    string Machine,
    int Number,
    DateTimeOffset ReceivedAt,
    DateTimeOffset CollectedAt,
    string? Agent,
    HostSection? Host,
    MemorySection? Memory,
    IReadOnlyList<DiskUsage>? Disks,
    IReadOnlyList<ContainerState>? Containers,
    IReadOnlyList<ListeningPort>? Listening,
    UpdatesSection? Updates,
    IReadOnlyList<MissingSection> Missing,
    IReadOnlyList<DriftShape> Drift);

/// <summary>Turns report rows into the two shapes, with the drift beside the whole one.</summary>
public sealed class ReportAssembler(DriftFinder drift)
{
    public static ReportSummaryShape Summary(Report report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var containers = report.Body.Containers;

        return new ReportSummaryShape(
            report.Number,
            report.ReceivedAt,
            report.CollectedAt,
            containers is null ? null : containers.Count(one => one.State is "running"),
            containers?.Count,
            report.Body.Disks is { Count: > 0 } disks ? disks.Max(disk => disk.Percent) : null,
            report.Body.Host?.Load1,
            report.Body.Updates?.RebootRequired);
    }

    /// <summary>
    /// The whole report, and what it and the record disagree about. The
    /// comparison is made here rather than by each client, which is what keeps
    /// the web application and <c>ha</c> from saying different things about one
    /// host (ADR 0015).
    /// </summary>
    public async Task<ReportShape> CompleteAsync(
        Machine machine, Report report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        return new ReportShape(
            machine.Key,
            report.Number,
            report.ReceivedAt,
            report.CollectedAt,
            report.Agent,
            report.Body.Host,
            report.Body.Memory,
            report.Body.Disks,
            report.Body.Containers,
            report.Body.Listening,
            report.Body.Updates,
            report.Body.Missing,
            await drift.BetweenAsync(machine, report, cancellationToken));
    }
}

/// <summary>
/// A machine's reports, newest first. Read with an ordinary user or agent
/// token, like everything else — and never with a machine token, which reads
/// nothing, its own reports included (ADR 0016).
/// </summary>
/// <remarks>
/// A machine that has never reported answers with an empty list rather than a
/// refusal. That is not an error but the ordinary state of a machine on which
/// no cron has been set up.
/// </remarks>
public sealed class ListReports(IMachines machines, IReports reports, InstanceSettings settings)
{
    public const int DefaultLimit = 50;

    public const int MaximumLimit = 200;

    /// <exception cref="Refusal"><c>validation</c> on <c>limit</c> or <c>offset</c>.</exception>
    public async Task<ReportPageShape> ExecuteAsync(
        string key, int? limit, int? offset, CancellationToken cancellationToken)
    {
        if (limit is <= 0)
        {
            throw Refusal.Validation("limit", "A limit is a count, and a count is at least one.");
        }

        if (offset is < 0)
        {
            throw Refusal.Validation("offset", "An offset is how many to skip, and is never negative.");
        }

        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        var (page, total) = await reports.ListAsync(
            machine.Id,
            offset ?? 0,
            Math.Min(limit ?? DefaultLimit, MaximumLimit),
            cancellationToken);

        return new ReportPageShape(total, [.. page.Select(ReportAssembler.Summary)]);
    }
}

/// <summary>The latest report of a machine, whole.</summary>
public sealed class ReadLatestReport(
    IMachines machines, IReports reports, ReportAssembler assembler, InstanceSettings settings)
{
    /// <exception cref="Refusal"><c>not-found</c> where the machine has never reported.</exception>
    public async Task<ReportShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        var report = await reports.LatestAsync(machine.Id, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"{machine.Key} has never reported.");

        return await assembler.CompleteAsync(machine, report, cancellationToken);
    }
}

/// <summary>One report of a machine by its number, whole.</summary>
public sealed class ReadReport(
    IMachines machines, IReports reports, ReportAssembler assembler, InstanceSettings settings)
{
    /// <exception cref="Refusal"><c>not-found</c> where there is no such report, swept included.</exception>
    public async Task<ReportShape> ExecuteAsync(string key, int number, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        var report = await reports.FindAsync(machine.Id, number, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"{machine.Key} has no report {number}.");

        return await assembler.CompleteAsync(machine, report, cancellationToken);
    }
}
