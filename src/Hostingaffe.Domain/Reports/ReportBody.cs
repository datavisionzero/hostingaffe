using Hostingaffe.Domain.Installations;

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
    public const int MaxListening = 128;

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

    /// <summary>
    /// What listens on the machine, one entry per port and protocol. Never a
    /// process: see <see cref="ListeningPort"/>.
    /// </summary>
    public IReadOnlyList<ListeningPort>? Listening { get; init; }

    public UpdatesSection? Updates { get; init; }

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

/// <summary>
/// One port the machine listens on, and how far the socket is bound
/// (<c>CONTEXT.md</c>, Report).
/// </summary>
/// <remarks>
/// <para>
/// <strong>No process, ever.</strong> Not the name, not the command line, not
/// the arguments. <c>ss -tulpn</c> shows another user's process only as root,
/// and the collector's promise is that it needs none; a section that were whole
/// on one machine and half empty on the next, depending on who the cron runs
/// as, would be worse than one that everywhere says the same. The comparison
/// this exists for runs against an installation's <c>ports</c>, which are ports
/// and not processes, so it loses nothing.
/// </para>
/// <para>
/// One entry per port and protocol, however many addresses the port is bound
/// to, and the widest binding wins: a port on <c>0.0.0.0</c> and on
/// <c>127.0.0.1</c> is <see cref="Binding.Public"/>, because that is the
/// honest answer to how far it is reachable.
/// </para>
/// </remarks>
public sealed record ListeningPort
{
    public int Port { get; init; }

    /// <summary>The transport, spelled as an installation's port spells it.</summary>
    public Protocol Protocol { get; init; }

    public Binding Binding { get; init; }
}

/// <summary>
/// How far a socket is bound, as a host can tell without asking anything but
/// the kernel. Closed, and deliberately <strong>not</strong>
/// <see cref="Scope"/>.
/// </summary>
/// <remarks>
/// A scope is what an operator decided a port is for — <c>private</c> means
/// "from my own network", which is a firewall's doing and invisible in a
/// listening socket. A binding is only what the socket says: either it is
/// reachable from beyond this machine, or it is not.
/// </remarks>
public enum Binding
{
    /// <summary>Bound to a wildcard or to an address other machines can reach.</summary>
    Public,

    /// <summary>Bound to loopback alone, and reachable from this machine only.</summary>
    Loopback,
}

/// <summary>
/// What the machine says about its own upkeep. Today one thing: whether it is
/// waiting for a restart.
/// </summary>
/// <remarks>
/// How many packages have an update is deliberately not here. Counting them
/// makes the collector distribution-dependent for the first time — apt, dnf,
/// apk, pacman, each with its own command and its own behaviour when the
/// package lists are old — and a host on which nothing ran <c>apt update</c>
/// for weeks would report nothing pending and lie in the most comforting way
/// there is. The restart is the part that is cheap, near enough the same
/// everywhere, and the part people forget.
/// </remarks>
public sealed record UpdatesSection
{
    /// <summary>
    /// Whether the machine is waiting for a restart. The section is absent
    /// where that cannot be told, and then <c>missing</c> says why — a
    /// <c>false</c> from a machine nobody could ask would be the worst of the
    /// three answers.
    /// </summary>
    public bool RebootRequired { get; init; }
}

/// <summary>A section the collector could not determine, and the reason it gives.</summary>
public sealed record MissingSection
{
    /// <summary>The name of the section, as the body spells it: <c>containers</c>.</summary>
    public string Section { get; init; } = null!;

    public string Reason { get; init; } = null!;
}
