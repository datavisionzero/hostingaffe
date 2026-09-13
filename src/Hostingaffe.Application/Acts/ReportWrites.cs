using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What a machine sends when it hands in a report. Closed, like every other
/// request object of this API, and closed section by section: a body that
/// quietly swallowed a field it did not know is exactly the catch-all
/// <see cref="ReportBody"/> exists not to become (ADR 0015).
/// </summary>
/// <remarks>
/// Numbers are numbers — bytes, not <c>42G</c> — and times are RFC 3339 like
/// everywhere else. Every section may be left out; what the collector could not
/// determine belongs in <c>missing</c>, with its reason, rather than nowhere.
/// </remarks>
public sealed record HandInReportRequest(
    DateTimeOffset? CollectedAt,
    string? Agent,
    HostSectionRequest? Host,
    MemorySectionRequest? Memory,
    IReadOnlyList<DiskRequest>? Disks,
    IReadOnlyList<ContainerRequest>? Containers,
    IReadOnlyList<MissingRequest>? Missing)
{
    /// <inheritdoc cref="ReportWrites.Closed"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <inheritdoc cref="HandInReportRequest"/>
public sealed record HostSectionRequest(
    string? Hostname,
    string? Os,
    string? Kernel,
    string? Arch,
    long? UptimeSeconds,
    double? Load1,
    double? Load5,
    double? Load15)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <inheritdoc cref="HandInReportRequest"/>
public sealed record MemorySectionRequest(
    long? TotalBytes,
    long? UsedBytes,
    long? AvailableBytes,
    long? SwapTotalBytes,
    long? SwapUsedBytes)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <inheritdoc cref="HandInReportRequest"/>
public sealed record DiskRequest(string? Mount, string? Device, long? SizeBytes, long? UsedBytes, int? Percent)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <inheritdoc cref="HandInReportRequest"/>
public sealed record ContainerRequest(
    string? Name,
    string? Image,
    string? State,
    string? Status,
    string? Health,
    int? Restarts,
    DateTimeOffset? StartedAt,
    IReadOnlyList<string>? Ports)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <inheritdoc cref="HandInReportRequest"/>
public sealed record MissingRequest(string? Section, string? Reason)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// Turns what arrived into the <see cref="ReportBody"/> the row holds, and
/// refuses at the door what the shape does not allow.
/// </summary>
public static class ReportWrites
{
    /// <summary>What a body may weigh, so that a collector that appended something large is caught.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>How often one machine may report, so that a cron running amok is caught.</summary>
    public static readonly TimeSpan MinimumBetweenReports = TimeSpan.FromMinutes(1);

    /// <exception cref="Refusal"><c>unknown-field</c>, naming the field and where it sat.</exception>
    public static void Closed(string where, Dictionary<string, JsonElement>? unknown)
    {
        if (unknown?.Keys.FirstOrDefault() is { } field)
        {
            throw new Refusal(
                RefusalCode.UnknownField,
                field switch
                {
                    "received_at" => "The instance sets received_at from its own clock; a report does not say when it arrived.",
                    "number" => "The instance numbers a report; it is not given.",
                    "machine" => "A report is for the machine its token belongs to, and names no other.",
                    _ => $"{where} has no field {field}.",
                },
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <exception cref="Refusal"><c>validation</c>, naming the field.</exception>
    public static ReportBody Body(HandInReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Closed("A report", request.UnknownFields);

        return new ReportBody
        {
            Host = HostSection(request.Host),
            Memory = MemorySection(request.Memory),
            Disks = Disks(request.Disks),
            Containers = Containers(request.Containers),
            Missing = Missing(request.Missing),
        };
    }

    private static HostSection? HostSection(HostSectionRequest? given)
    {
        if (given is null)
        {
            return null;
        }

        Closed("A report's host", given.UnknownFields);

        return new HostSection
        {
            Hostname = Line("host.hostname", given.Hostname),
            Os = Line("host.os", given.Os),
            Kernel = Line("host.kernel", given.Kernel),
            Arch = Line("host.arch", given.Arch),
            UptimeSeconds = NotNegative("host.uptime_seconds", given.UptimeSeconds),
            Load1 = Load("host.load1", given.Load1),
            Load5 = Load("host.load5", given.Load5),
            Load15 = Load("host.load15", given.Load15),
        };
    }

    private static MemorySection? MemorySection(MemorySectionRequest? given)
    {
        if (given is null)
        {
            return null;
        }

        Closed("A report's memory", given.UnknownFields);

        return new MemorySection
        {
            TotalBytes = NotNegative("memory.total_bytes", given.TotalBytes),
            UsedBytes = NotNegative("memory.used_bytes", given.UsedBytes),
            AvailableBytes = NotNegative("memory.available_bytes", given.AvailableBytes),
            SwapTotalBytes = NotNegative("memory.swap_total_bytes", given.SwapTotalBytes),
            SwapUsedBytes = NotNegative("memory.swap_used_bytes", given.SwapUsedBytes),
        };
    }

    private static IReadOnlyList<DiskUsage>? Disks(IReadOnlyList<DiskRequest>? given)
    {
        if (given is null)
        {
            return null;
        }

        AtMost("disks", given.Count, ReportBody.MaxDisks);

        return
        [
            .. given.Select(disk =>
            {
                Closed("A report's disk", disk.UnknownFields);

                return new DiskUsage
                {
                    Mount = Required("disks.mount", disk.Mount),
                    Device = Line("disks.device", disk.Device),
                    SizeBytes = NotNegative("disks.size_bytes", disk.SizeBytes),
                    UsedBytes = NotNegative("disks.used_bytes", disk.UsedBytes),
                    Percent = Percent(disk.Percent),
                };
            }),
        ];
    }

    private static IReadOnlyList<ContainerState>? Containers(IReadOnlyList<ContainerRequest>? given)
    {
        if (given is null)
        {
            return null;
        }

        AtMost("containers", given.Count, ReportBody.MaxContainers);

        return
        [
            .. given.Select(container =>
            {
                Closed("A report's container", container.UnknownFields);
                AtMost("containers.ports", container.Ports?.Count ?? 0, ReportBody.MaxPorts);

                return new ContainerState
                {
                    Name = Required("containers.name", container.Name),
                    Image = Line("containers.image", container.Image),
                    State = Line("containers.state", container.State),
                    Status = Line("containers.status", container.Status),
                    Health = Line("containers.health", container.Health),
                    Restarts = NotNegative("containers.restarts", container.Restarts),
                    StartedAt = container.StartedAt,
                    Ports = container.Ports is null
                        ? null
                        : [.. container.Ports.Select(port => Required("containers.ports", port))],
                };
            }),
        ];
    }

    private static IReadOnlyList<MissingSection> Missing(IReadOnlyList<MissingRequest>? given)
    {
        if (given is null)
        {
            return [];
        }

        AtMost("missing", given.Count, ReportBody.MaxMissing);

        return
        [
            .. given.Select(missing =>
            {
                Closed("A report's missing section", missing.UnknownFields);

                return new MissingSection
                {
                    Section = Required("missing.section", missing.Section),
                    Reason = Validated.Field(
                        "missing.reason",
                        () => Fields.Line(
                            (missing.Reason ?? string.Empty).Trim(), ReportBody.ReasonMaxLength, "A reason")),
                };
            }),
        ];
    }

    private static string Required(string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw Refusal.Validation(field, "The value is required.")
            : Validated.Field(field, () => Fields.Line(value.Trim(), ReportBody.LineMaxLength, "The value"));

    private static string? Line(string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Validated.Field(field, () => Fields.Line(value.Trim(), ReportBody.LineMaxLength, "The value"));

    private static long? NotNegative(string field, long? value) =>
        value is < 0 ? throw Refusal.Validation(field, "The value is a count of bytes and is not negative.") : value;

    private static int? NotNegative(string field, int? value) =>
        value is < 0 ? throw Refusal.Validation(field, "The value is a count and is not negative.") : value;

    private static double? Load(string field, double? value) =>
        value is null
            ? null
            : value is < 0 or double.NaN or double.PositiveInfinity or double.NegativeInfinity
                ? throw Refusal.Validation(field, "A load average is a number and is not negative.")
                : value;

    private static int? Percent(int? value) =>
        value is null or (>= 0 and <= 100)
            ? value
            : throw Refusal.Validation("disks.percent", "A percentage is between 0 and 100.");

    private static void AtMost(string field, int count, int limit)
    {
        if (count > limit)
        {
            throw Refusal.Validation(field, $"A report carries at most {limit} of these, and this one carries {count}.");
        }
    }
}
