using System.Security.Cryptography;
using System.Text;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Reports;

using File = Hostingaffe.Domain.Files.File;

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
    AnchorKind SubjectKind,
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
    /// A port an installation or the machine says it listens on against what
    /// the machine actually has a socket for — and, where the machine keeps its
    /// own ports, one bound in public that stands in no record at all.
    /// </summary>
    Port,

    /// <summary>
    /// A file of the record against the digest the machine reported for the
    /// path <c>files sync</c> wrote it to (ADR 0017).
    /// </summary>
    File,
}

/// <summary>
/// The comparison itself, in the Application layer because that is the one
/// place that sees both sides. The web application and the CLI are handed the
/// result rather than each building it, which is how the two are kept from
/// saying different things about the same host.
/// </summary>
public sealed class DriftFinder(
    IInstallations installations, ISoftware software, IDeployments deployments, IFiles files)
{
    public async Task<IReadOnlyList<DriftShape>> BetweenAsync(
        Machine machine, Report report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(report);

        var drift = new List<DriftShape>();

        Facts(machine, report, drift);

        if (report.Body is { Containers: null, Listening: null, Files: null })
        {
            return drift;
        }

        var rows = await installations.OnMachineAsync(machine.Id, null, cancellationToken);

        Ports(machine, rows, report, drift);

        await AboutFilesAsync(rows, report, drift, cancellationToken);

        if (rows.Count == 0 || report.Body.Containers is not { } containers)
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
                DriftKind.Fact,
                AnchorKind.Machine,
                machine.Key,
                "os",
                machine.Os,
                machine.MeasuredAt,
                host.Os,
                report.ReceivedAt));
        }

        // `arch` is a closed set in the record and whatever `uname -m` says on
        // the machine, so the two are compared through the spellings that mean
        // the same thing rather than as text.
        if (machine.Arch is { } arch && Architecture(host.Arch) is { } reported && reported != arch)
        {
            drift.Add(new DriftShape(
                DriftKind.Fact,
                AnchorKind.Machine,
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
    /// <strong>A machine keeps its own ports, and an empty list says nothing
    /// rather than "none".</strong> What belongs to the machine and to no
    /// installation of it — SSH, a Wireguard endpoint — is written down at the
    /// machine, and that is what makes "listens in public and stands in no
    /// record" a drift somebody can clear: either the port is written down or
    /// it is closed. A machine nobody has filled that list in for is not told
    /// that every port it has is undocumented — the comparison is simply not
    /// made, because a drift nobody can clear teaches people to stop reading
    /// the list. It is not a suppression list: no single port and no single
    /// finding is silenced, and the three comparisons above run either way.
    /// </para>
    /// </remarks>
    private static void Ports(
        Machine machine, IReadOnlyList<Installation> rows, Report report, List<DriftShape> drift)
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

        // What the record claims, from both sides of it: an active
        // installation's ports and the machine's own. A planned installation is
        // passed over — it is not supposed to be listening — but its ports
        // still count as claimed, because a port somebody wrote down is not an
        // undocumented one whatever the installation's state says.
        var claimed = new HashSet<(int Port, Protocol Protocol)>();

        foreach (var installation in rows)
        {
            foreach (var port in installation.Ports)
            {
                claimed.Add((port.Number, port.Protocol));

                if (installation.Status is Status.Active)
                {
                    Listening(
                        AnchorKind.Installation,
                        installation.Key,
                        port,
                        installation.UpdatedAt,
                        heard,
                        report.ReceivedAt,
                        drift);
                }
            }
        }

        foreach (var port in machine.Ports)
        {
            claimed.Add((port.Number, port.Protocol));
            Listening(AnchorKind.Machine, machine.Key, port, machine.UpdatedAt, heard, report.ReceivedAt, drift);
        }

        Undocumented(machine, listening, claimed, report.ReceivedAt, drift);
    }

    /// <summary>
    /// The question a documentation of rented machines is kept for: what is
    /// reachable from outside that nobody wrote down.
    /// </summary>
    /// <remarks>
    /// Only where the machine keeps ports of its own — that is what makes every
    /// finding here resolvable, and what keeps a machine nobody maintains the
    /// list for quiet. A <c>loopback</c> socket is not in it: it reaches nothing
    /// off this machine, and a record of what an operator rents is not a process
    /// list.
    /// </remarks>
    private static void Undocumented(
        Machine machine,
        IReadOnlyList<ListeningPort> listening,
        IReadOnlySet<(int Port, Protocol Protocol)> claimed,
        DateTimeOffset receivedAt,
        List<DriftShape> drift)
    {
        if (machine.Ports.Count == 0)
        {
            return;
        }

        foreach (var one in listening)
        {
            if (one.Binding is not Binding.Public || claimed.Contains((one.Port, one.Protocol)))
            {
                continue;
            }

            // The record says nothing, which is the whole finding: `record` is
            // null and `record_at` with it, because there is no line to date.
            drift.Add(new DriftShape(
                DriftKind.Port,
                AnchorKind.Machine,
                machine.Key,
                $"port {one.Port}/{Spelling.Of(one.Protocol)}",
                null,
                null,
                Spelling.Of(one.Binding),
                receivedAt));
        }
    }

    private static void Listening(
        AnchorKind subjectKind,
        string subject,
        Port port,
        DateTimeOffset recordedAt,
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
                DriftKind.Port, subjectKind, subject, field, recorded, recordedAt, null, receivedAt));
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
                DriftKind.Port, subjectKind, subject, field, recorded, recordedAt, Spelling.Of(binding), receivedAt));
        }
    }

    /// <summary>
    /// What the record has in an installation's directory against what the
    /// machine reports lying there — the drift check for configuration that
    /// VISION 15.1 left open, answered without a token that reads (ADR 0017).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The comparison runs for exactly the directories the report
    /// names</strong>, and for no others. A machine whose cron was given no
    /// <c>--sync-dir</c> reports no files and hears nothing about them: the
    /// same rule the machine's own ports keep, and for the same reason — a
    /// drift nobody can clear teaches people to stop reading the list. A
    /// directory it does name is compared whole, and an empty one is a claim
    /// too: "I hold this installation's files here and have none of them".
    /// </para>
    /// <para>
    /// Both sides are named as digests, because that is the one value the two
    /// sides have in common: a file on a host has no revision, and a revision
    /// is not what a host can be asked for. <strong>Only the content is
    /// compared</strong>, never the mode bit — the manifest hashes bytes, and
    /// <c>files sync</c> puts the record's mode on every run anyway.
    /// </para>
    /// <para>
    /// An installation the report names that is not on this machine is passed
    /// over in silence. It is a cron pointed at the wrong directory, which is a
    /// mistake on the host and not a disagreement between the two sides.
    /// </para>
    /// </remarks>
    private async Task AboutFilesAsync(
        IReadOnlyList<Installation> rows,
        Report report,
        List<DriftShape> drift,
        CancellationToken cancellationToken)
    {
        if (report.Body.Files is not { Count: > 0 } directories)
        {
            return;
        }

        var byKey = rows.ToDictionary(one => one.Key, StringComparer.Ordinal);
        var named = directories
            .Where(one => byKey.ContainsKey(one.Installation))
            .Select(one => byKey[one.Installation].Id)
            .ToHashSet();

        if (named.Count == 0)
        {
            return;
        }

        var recorded = (await files.UnderAsync(AnchorKind.Installation, named, null, cancellationToken))
            .GroupBy(one => one.InstallationId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var directory in directories)
        {
            if (!byKey.TryGetValue(directory.Installation, out var installation))
            {
                continue;
            }

            InDirectory(
                installation,
                directory,
                recorded.TryGetValue(installation.Id, out var held) ? held : [],
                report.ReceivedAt,
                drift);
        }
    }

    /// <summary>One directory, path by path, in an order a person can read twice.</summary>
    private static void InDirectory(
        Installation installation,
        SyncedDirectory directory,
        IReadOnlyList<File> recorded,
        DateTimeOffset receivedAt,
        List<DriftShape> drift)
    {
        var held = recorded.ToDictionary(one => one.Path, StringComparer.Ordinal);
        var found = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var file in directory.Files)
        {
            found[file.Path] = file.Sha256;
        }

        foreach (var path in held.Keys.Concat(found.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var record = held.TryGetValue(path, out var file) ? Digest(file.Content) : null;
            found.TryGetValue(path, out var reported);

            // What lies there is what the record has, or nothing lies there and
            // the record has nothing either: the manifest still names a file
            // both sides have let go of, and there is nothing to clear.
            if (record == reported)
            {
                continue;
            }

            drift.Add(new DriftShape(
                DriftKind.File,
                AnchorKind.Installation,
                installation.Key,
                $"file {path}",
                Short(record),
                file?.UpdatedAt,
                Short(reported),
                receivedAt));
        }
    }

    /// <summary>
    /// The digest of what the record holds, computed the way the host computes
    /// the one it reports: over the bytes of the content and nothing else.
    /// </summary>
    private static string Digest(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    /// As much of a digest as a person compares by eye. The whole one is in
    /// neither side's way: what a drift is for is saying that the two differ,
    /// and twelve characters say it the way a commit does.
    /// </summary>
    private static string? Short(string? digest) =>
        digest is null ? null : digest[..Math.Min(12, digest.Length)];

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
            DriftKind.Version,
            AnchorKind.Installation,
            installation.Key,
            "version",
            deployed.Version,
            deployed.At,
            tag,
            receivedAt));
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
            AnchorKind.Installation,
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
