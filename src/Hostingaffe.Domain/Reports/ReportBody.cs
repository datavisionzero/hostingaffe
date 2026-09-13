namespace Hostingaffe.Domain.Reports;

/// <summary>
/// What a machine said about itself: a closed set of sections, each of which
/// may be missing (<c>CONTEXT.md</c>, Report; VISION 7).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Closed is the whole point.</strong> There is no free-form field
/// beside these, because a body that takes anything is a metric database in six
/// months, and a metric database wants thresholds (VISION 5).
/// </para>
/// <para>
/// A section the collector could not determine is absent and named in
/// <see cref="Missing"/> with its reason. A host without Docker reports no
/// containers and says why; it does not fail.
/// </para>
/// <para>
/// Nothing derivable is carried: how many containers run of how many is counted
/// from <see cref="Containers"/> where it is read, for the reason
/// <c>needed_by</c> is derived — two numbers for one fact are one disagreement
/// away from being useless (ADR 0014).
/// </para>
/// </remarks>
public sealed record ReportBody
{
    /// <summary>What a report may carry of each kind, so that a body has an end.</summary>
    public const int MaxDisks = 64;

    /// <inheritdoc cref="MaxDisks"/>
    public const int MaxContainers = 500;

    /// <inheritdoc cref="MaxDisks"/>
    public const int MaxPorts = 64;

    /// <inheritdoc cref="MaxDisks"/>
    public const int MaxMissing = 8;

    /// <summary>What one line of a section fits in.</summary>
    public const int LineMaxLength = 200;

    /// <summary>What a reason for a missing section fits in.</summary>
    public const int ReasonMaxLength = 300;

    public HostSection? Host { get; init; }

    public MemorySection? Memory { get; init; }

    /// <summary>The real mounts; tmpfs, devtmpfs, overlay and squashfs are not real.</summary>
    public IReadOnlyList<DiskUsage>? Disks { get; init; }

    public IReadOnlyList<ContainerState>? Containers { get; init; }

    /// <summary>What could not be determined, and why. Never null; empty is the happy case.</summary>
    public IReadOnlyList<MissingSection> Missing { get; init; } = [];

    /// <summary>Nothing at all — a machine that answered and had nothing to say.</summary>
    public static ReportBody Empty { get; } = new();
}

/// <summary>The machine itself: who it says it is, and how busy it has been.</summary>
public sealed record HostSection
{
    public string? Hostname { get; init; }

    public string? Os { get; init; }

    public string? Kernel { get; init; }

    /// <summary>What <c>uname -m</c> reports, as it reports it — not the record's closed set.</summary>
    public string? Arch { get; init; }

    /// <summary>
    /// How long the machine has been up. Seconds rather than a boot time,
    /// because <c>/proc/uptime</c> gives this one directly and a boot time
    /// computed from it would inherit a wrong clock.
    /// </summary>
    public long? UptimeSeconds { get; init; }

    public double? Load1 { get; init; }

    public double? Load5 { get; init; }

    public double? Load15 { get; init; }
}

/// <summary>Bytes, never a formatted size: <c>42G</c> is a rendering.</summary>
public sealed record MemorySection
{
    public long? TotalBytes { get; init; }

    public long? UsedBytes { get; init; }

    public long? AvailableBytes { get; init; }

    public long? SwapTotalBytes { get; init; }

    public long? SwapUsedBytes { get; init; }
}

/// <summary>One real mount, as the host measured it.</summary>
public sealed record DiskUsage
{
    public string Mount { get; init; } = null!;

    public string? Device { get; init; }

    public long? SizeBytes { get; init; }

    public long? UsedBytes { get; init; }

    /// <summary>What the host rounded it to, kept as it came rather than recomputed.</summary>
    public int? Percent { get; init; }
}

/// <summary>
/// One container, with its image's <strong>tag</strong> — which is what makes
/// the comparison against the installation's latest deployment possible, and is
/// why a software's <c>image</c> carries none and this one does
/// (<c>CONTEXT.md</c>, Report).
/// </summary>
public sealed record ContainerState
{
    public string Name { get; init; } = null!;

    /// <summary>The image reference with its tag, as the host reported it.</summary>
    public string? Image { get; init; }

    /// <summary>Docker's word — <c>running</c>, <c>exited</c>, <c>restarting</c>. Not a closed set of this model.</summary>
    public string? State { get; init; }

    /// <summary>Docker's sentence — <c>Up 3 days</c>.</summary>
    public string? Status { get; init; }

    /// <summary><c>healthy</c>, <c>unhealthy</c>, <c>starting</c>, or nothing where there is no health check.</summary>
    public string? Health { get; init; }

    public int? Restarts { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>As the host spells them — <c>0.0.0.0:443-&gt;443/tcp</c>.</summary>
    public IReadOnlyList<string>? Ports { get; init; }
}

/// <summary>A section the collector could not determine, and the reason it gives.</summary>
public sealed record MissingSection
{
    /// <summary>The name of the section, as the body spells it: <c>containers</c>.</summary>
    public string Section { get; init; } = null!;

    public string Reason { get; init; } = null!;
}
