using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Reports;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What the record and the machine disagree about, computed on read and stored
/// nowhere (ADR 0015).
/// </summary>
/// <remarks>
/// <para>
/// It is the point of keeping a report beside the record rather than in it:
/// because both sides are there, they can be compared. Every drift names both
/// sides and how old each is — what the record says and since when, what the
/// machine reported and when it reported it. <strong>Which side is right the
/// product does not say</strong>: that is the decision ADR 0015 leaves to a
/// person or an agent, and there is no button that pulls one after the other.
/// </para>
/// <para>
/// Disk, memory and load have no other side in the record and therefore make no
/// drift. They are shown, not compared, and "91 per cent full" is a number a
/// person reads rather than a disagreement.
/// </para>
/// </remarks>
public sealed record DriftShape(
    DriftKind Kind,
    string Subject,
    string Field,
    string? Record,
    DateTimeOffset? RecordAt,
    string? Reported,
    DateTimeOffset ReportedAt);

/// <summary>What a drift is about. Closed, like every other set of the model.</summary>
public enum DriftKind
{
    /// <summary>The image tag of a container against the version of the installation's latest deployment.</summary>
    Version,

    /// <summary>An installation the record calls active whose container is not running.</summary>
    Container,

    /// <summary>A fact of the machine — <c>os</c>, <c>arch</c> — against what the host says it is.</summary>
    Fact,

    /// <summary>
    /// A port an installation says it listens on against what the machine
    /// actually has a socket for.
    /// </summary>
    Port,
}

/// <summary>
/// The comparison itself, in the Application layer because that is the one
/// place that sees both sides. The web application and the CLI are handed the
/// result rather than each building it, which is how the two are kept from
/// saying different things about the same host.
/// </summary>
public sealed class DriftFinder(IInstallations installations, ISoftware software, IDeployments deployments)
{
    public async Task<IReadOnlyList<DriftShape>> BetweenAsync(
        Machine machine, Report report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        var drift = new List<DriftShape>();

        Facts(machine, report, drift);

        if (report.Body is { Containers: null, Listening: null })
        {
            return drift;
        }

        var rows = await installations.OnMachineAsync(machine.Id, null, cancellationToken);
        if (rows.Count == 0)
        {
            return drift;
        }

        Ports(rows, report, drift);

        if (report.Body.Containers is not { } containers)
        {
            return drift;
        }

        var images = (await software.ListAsync(cancellationToken))
            .Where(one => !string.IsNullOrWhiteSpace(one.Image))
            .ToDictionary(one => one.Id, one => one.Image!);

        var all = await deployments.ListAsync(rows.Select(one => one.Id), cancellationToken);
        var byInstallation = all.GroupBy(one => one.InstallationId).ToDictionary(group => group.Key, group => group.ToList());

        await AboutContainersAsync(rows, images, byInstallation, containers, report.ReceivedAt, drift);

        return drift;
    }

    /// <summary>
    /// The case VISION 15.1 describes in as many words: what somebody wrote
    /// down, with the date they last checked it, against what the machine says
    /// it is.
    /// </summary>
    private static void Facts(Machine machine, Report report, List<DriftShape> drift)
    {
        if (report.Body.Host is not { } host)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(machine.Os) && !string.IsNullOrWhiteSpace(host.Os)
            && !string.Equals(machine.Os.Trim(), host.Os.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            drift.Add(new DriftShape(
                DriftKind.Fact, machine.Key, "os", machine.Os, machine.MeasuredAt, host.Os, report.ReceivedAt));
        }

        // `arch` is a closed set in the record and whatever `uname -m` says on
        // the machine, so the two are compared through the spellings that mean
        // the same thing rather than as text.
        if (machine.Arch is { } arch && Architecture(host.Arch) is { } reported && reported != arch)
        {
            drift.Add(new DriftShape(
                DriftKind.Fact,
                machine.Key,
                "arch",
                Spelling.Of(arch),
                machine.MeasuredAt,
                Spelling.Of(reported),
                report.ReceivedAt));
        }
    }

    /// <summary>
    /// What the record says an installation listens on against what the machine
    /// has a socket for — the case VISION 16 asks as "what listens on 18502",
    /// answered from what is the case rather than from what somebody typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="Scope"/> is not a <see cref="Binding"/> and the two are not
    /// compared as if they were. <c>public</c> and <c>private</c> both need a
    /// socket bound beyond loopback — how much further is a firewall's doing,
    /// which no listening socket shows — so both are read as "reachable from
    /// off this machine". <c>internal</c> means the port never reaches the host
    /// at all, so hearing nothing is the agreement and hearing it in public is
    /// the disagreement.
    /// </para>
    /// <para>
    /// An installation the record does not call <c>active</c> is passed over: a
    /// planned one is not supposed to be listening, and saying so every quarter
    /// of an hour would be noise, not drift.
    /// </para>
    /// <para>
    /// <strong>A port that listens and belongs to no installation is not
    /// drift.</strong> It is the question this section was wanted for, but the
    /// record has no place to put a machine's own ports — SSH is in no
    /// installation — so the statement could never be resolved by anybody, and
    /// a drift nobody can clear teaches people to stop reading the list. The
    /// <c>listening</c> section is shown whole instead, and a person reads it.
    /// </para>
    /// </remarks>
    private static void Ports(IReadOnlyList<Installation> rows, Report report, List<DriftShape> drift)
    {
        if (report.Body.Listening is not { } listening)
        {
            return;
        }

        var heard = new Dictionary<(int Port, Protocol Protocol), Binding>();
        foreach (var one in listening)
        {
            heard[(one.Port, one.Protocol)] = one.Binding;
        }

        foreach (var installation in rows.Where(one => one.Status is Status.Active))
        {
            foreach (var port in installation.Ports)
            {
                Listening(installation, port, heard, report.ReceivedAt, drift);
            }
        }
    }

    private static void Listening(
        Installation installation,
        Port port,
        IReadOnlyDictionary<(int Port, Protocol Protocol), Binding> heard,
        DateTimeOffset receivedAt,
        List<DriftShape> drift)
    {
        var field = $"port {port.Number}/{Spelling.Of(port.Protocol)}";
        var recorded = Spelling.Of(port.Scope);

        if (!heard.TryGetValue((port.Number, port.Protocol), out var binding))
        {
            // An `internal` port is inside a container network and the host
            // never sees it; silence is what the record led one to expect.
            if (port.Scope is Scope.Internal)
            {
                return;
            }

            drift.Add(new DriftShape(
                DriftKind.Port, installation.Key, field, recorded, installation.UpdatedAt, null, receivedAt));
            return;
        }

        var disagrees = port.Scope switch
        {
            Scope.Internal => binding is Binding.Public,
            _ => binding is Binding.Loopback,
        };

        if (disagrees)
        {
            drift.Add(new DriftShape(
                DriftKind.Port,
                installation.Key,
                field,
                recorded,
                installation.UpdatedAt,
                Spelling.Of(binding),
                receivedAt));
        }
    }

    /// <summary>What a host calls an architecture, as the record's closed set, or nothing where it is neither.</summary>
    private static Arch? Architecture(string? said) => said?.Trim().ToLowerInvariant() switch
    {
        "x86_64" or "amd64" => Arch.Amd64,
        "aarch64" or "arm64" => Arch.Arm64,
        _ => null,
    };

    private async Task AboutContainersAsync(
        IReadOnlyList<Installation> rows,
        IReadOnlyDictionary<Guid, string> images,
        IReadOnlyDictionary<Guid, List<Deployment>> byInstallation,
        IReadOnlyList<ContainerState> containers,
        DateTimeOffset receivedAt,
        List<DriftShape> drift)
    {
        await Task.CompletedTask;

        // The assignment runs over the image name without its tag, which the
        // software already carries. Nothing is guessed and nothing is stored:
        // the connection is made on read and is nowhere a column (ADR 0015).
        var byImage = new Dictionary<string, List<Installation>>(StringComparer.OrdinalIgnoreCase);
        foreach (var installation in rows)
        {
            if (images.TryGetValue(installation.SoftwareId, out var image))
            {
                byImage.TryAdd(image.Trim(), []);
                byImage[image.Trim()].Add(installation);
            }
        }

        var containersByImage = new Dictionary<string, List<ContainerState>>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in containers.Where(one => !string.IsNullOrWhiteSpace(one.Image)))
        {
            var name = WithoutTag(container.Image!);
            containersByImage.TryAdd(name, []);
            containersByImage[name].Add(container);
        }

        foreach (var (image, installations) in byImage)
        {
            if (!containersByImage.TryGetValue(image, out var matching))
            {
                // An installation with no container of its image says nothing:
                // it may not be containerised, it may be planned, and a claim
                // either way would be a guess.
                continue;
            }

            // Ambiguous: two installations of the same software on one machine,
            // or two containers out of one image. The report is shown and
            // nothing is claimed — a wrong sentence is worse than none, and
            // whoever wants to know reads the two rows beside each other.
            if (installations.Count != 1 || matching.Count != 1)
            {
                continue;
            }

            var installation = installations[0];
            var container = matching[0];

            Version(installation, container, byInstallation, receivedAt, drift);
            Running(installation, container, receivedAt, drift);
        }
    }

    /// <summary>
    /// The most valuable line of the whole feature: the record says logaffe-prod
    /// runs 1.4.0, the machine reports 1.3.2.
    /// </summary>
    private static void Version(
        Installation installation,
        ContainerState container,
        IReadOnlyDictionary<Guid, List<Deployment>> byInstallation,
        DateTimeOffset receivedAt,
        List<DriftShape> drift)
    {
        var deployed = byInstallation.TryGetValue(installation.Id, out var rows) ? Derived.Latest(rows) : null;
        if (deployed is null)
        {
            return;
        }

        var tag = Tag(container.Image);
        if (tag is null || string.Equals(tag, deployed.Version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        drift.Add(new DriftShape(
            DriftKind.Version, installation.Key, "version", deployed.Version, deployed.At, tag, receivedAt));
    }

    /// <summary>The record says active, the machine says the container is not running. A statement, not an alarm.</summary>
    private static void Running(
        Installation installation, ContainerState container, DateTimeOffset receivedAt, List<DriftShape> drift)
    {
        if (installation.Status is not Status.Active || container.State is "running")
        {
            return;
        }

        drift.Add(new DriftShape(
            DriftKind.Container,
            installation.Key,
            "status",
            Spelling.Of(installation.Status),
            installation.UpdatedAt,
            container.State,
            receivedAt));
    }

    /// <summary>
    /// The image without its tag — which is what a software carries, and what
    /// the two sides are matched on. A digest is not a tag and is cut the same
    /// way; a registry port keeps its colon, because a colon before a slash is
    /// part of the host.
    /// </summary>
    public static string WithoutTag(string image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var name = image.Trim();
        var digest = name.IndexOf('@', StringComparison.Ordinal);
        if (digest >= 0)
        {
            name = name[..digest];
        }

        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? name[..colon] : name;
    }

    /// <summary>The tag of an image reference, or nothing where it carries none.</summary>
    public static string? Tag(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        var name = image.Trim();
        var digest = name.IndexOf('@', StringComparison.Ordinal);
        if (digest >= 0)
        {
            name = name[..digest];
        }

        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? name[(colon + 1)..] : null;
    }
}
