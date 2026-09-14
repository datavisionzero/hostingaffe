using System.Globalization;
using System.Text;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Domain.Reports;

using File = Hostingaffe.Domain.Files.File;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// Everything recorded about one machine, as the Markdown an agent reads before
/// it touches the host (VISION 6.1, 16).
/// </summary>
/// <remarks>
/// The document is assembled here rather than by the caller. The command is
/// measured by what an operation costs in round trips and context, and a client
/// that built this would ask about thirty times — the machine, the installation
/// list, then every installation, its deployments, its files and its pages,
/// with a call per page for the Markdown a slim list does not carry. The web
/// application's machine screen is the same assembly, and one of them is what
/// keeps the two saying the same thing.
/// </remarks>
public sealed record MachineContextShape(string Key, string Document);

/// <summary>
/// The document, in the order that brings first what is needed first: the
/// machine, its installations, the software they are of, the machine's own
/// files, the pages that hang on any of it, and the instance's decisions —
/// the rules that hold on every host.
/// </summary>
/// <remarks>
/// <para>
/// <strong>File contents are not in it.</strong> They are one <c>ha files
/// get</c> away, and they are what would fill a context window.
/// </para>
/// <para>
/// <strong>Each installation says what it depends on and what depends on it</strong>,
/// which is the question an agent asks before it touches anything: what falls
/// out if I restart this. An installation on another machine is named with that
/// machine, because a bare key from another host is one the reader of this
/// document cannot look up in it — and nothing else of it is carried, so a
/// shared proxy does not drag seven foreign records in behind it (ADR 0014).
/// </para>
/// </remarks>
public sealed class ReadMachineContext(
    IMachines machines,
    IInstallations installations,
    ISoftware software,
    IFiles files,
    IDeployments deployments,
    IPages pages,
    IReports reports,
    DriftFinder drift,
    InstanceSettings settings,
    TimeProvider clock)
{
    /// <summary>
    /// How many deployments of an installation the document carries. What ran
    /// before that is a `ha deploy list` away, and the note of any of them a
    /// `ha deploy view`.
    /// </summary>
    public const int RecentDeployments = 5;

    /// <summary>
    /// How many containers the report section names one by one before it only
    /// counts them. A host with thirty containers would otherwise spend the
    /// budget of VISION 16 on a list that changes no decision; what is always
    /// named is every container that is <em>not</em> running, because that is
    /// the one an agent about to work here has to know about.
    /// </summary>
    public const int NamedContainers = 12;

    /// <summary>
    /// How many listening ports the report section names before it only counts
    /// them. The same budget as the containers, for the same reason (VISION 16).
    /// </summary>
    public const int NamedPorts = 24;

    /// <summary>
    /// Past this, a report is given with its age and not set down as the
    /// present. An agent that concluded from a three-week-old report what is
    /// running now would be worse off than one that knew nothing.
    /// </summary>
    public static readonly TimeSpan Recent = TimeSpan.FromDays(1);

    public async Task<MachineContextShape> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);

        var installed = Ordered(await installations.OnMachineAsync(machine.Id, null, cancellationToken));
        var ids = installed.Select(one => one.Id).ToArray();

        var recorded = await deployments.ListAsync(ids, cancellationToken);
        var underInstallations = await files.UnderAsync(AnchorKind.Installation, ids, null, cancellationToken);
        var underMachine = await files.UnderAsync(AnchorKind.Machine, [machine.Id], null, cancellationToken);

        var used = installed.Select(one => one.SoftwareId).ToHashSet();
        var programs = (await software.ListAsync(cancellationToken))
            .Where(one => used.Contains(one.Id))
            .OrderBy(one => one.Key, StringComparer.Ordinal)
            .ToArray();

        var hostKey = machine.HostId is { } on
            ? (await machines.KeysAsync([on], cancellationToken)).GetValueOrDefault(on)
            : null;

        var edge = await EdgeAsync(installed, cancellationToken);

        var attached = await AttachedAsync(machine, installed, cancellationToken);
        var rules = (await pages.ListAsync(null, PageKind.Decision, null, cancellationToken))
            .Where(page => !page.Attached)
            .OrderBy(page => page.Slug, StringComparer.Ordinal)
            .ToArray();

        var latest = await reports.LatestAsync(machine.Id, cancellationToken);
        var disagreements = latest is null
            ? []
            : await drift.BetweenAsync(machine, latest, cancellationToken);

        var document = new StringBuilder();
        Head(document, machine, hostKey);
        Reported(document, latest, disagreements, clock.GetUtcNow());
        Installations(document, installed, programs, recorded, underInstallations, edge);
        Software(document, programs);
        MachineFiles(document, machine, underMachine);
        Pages(document, attached);
        Rules(document, rules);

        return new MachineContextShape(machine.Key, document.ToString().TrimEnd() + "\n");
    }

    private static IReadOnlyList<Installation> Ordered(IEnumerable<Installation> rows) =>
        [.. rows.OrderBy(one => one.Key, StringComparer.Ordinal)];

    /// <summary>
    /// How every installation of this machine is named in the document: by its
    /// key, and by its key and its machine where it lies on another one.
    /// </summary>
    /// <remarks>
    /// Both ends of the edge are read at once — what these installations depend
    /// on, and what depends on them — and every id either end names that is not
    /// on this machine is looked up once, with the machine it lies on. A
    /// dependency the purge has already taken is in no map and is left out
    /// rather than printed as a question mark: the record no longer says it.
    /// </remarks>
    private async Task<Edge> EdgeAsync(
        IReadOnlyList<Installation> installed, CancellationToken cancellationToken)
    {
        var here = installed.ToDictionary(one => one.Id, one => one.Key);

        var dependents = await installations.DependentsAsync(here.Keys, cancellationToken);

        var named = installed
            .SelectMany(one => one.DependsOn.Select(dependency => dependency.DependsOnId))
            .Concat(dependents.SelectMany(pair => pair.Value))
            .Where(id => !here.ContainsKey(id))
            .Distinct()
            .ToArray();

        var elsewhere = await installations.FindManyAsync(named, cancellationToken);
        var hosts = await machines.KeysAsync(elsewhere.Select(one => one.MachineId), cancellationToken);

        var names = new Dictionary<Guid, string>(here);
        foreach (var one in elsewhere)
        {
            names[one.Id] = hosts.GetValueOrDefault(one.MachineId) is { } host
                ? $"{one.Key} (on {host})"
                : one.Key;
        }

        return new Edge(names, dependents);
    }

    /// <summary>
    /// What each installation of this machine is called in the document, and
    /// which installations depend on each of them.
    /// </summary>
    private sealed record Edge(
        IReadOnlyDictionary<Guid, string> Names,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Dependents)
    {
        public IReadOnlyList<string> Of(IEnumerable<Guid> ids) =>
        [
            .. ids.Select(id => Names.GetValueOrDefault(id))
                .OfType<string>()
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The pages that hang on the machine or on anything installed on it, each
    /// asked for by the anchor it hangs on, so that nothing but this machine's
    /// wiki is read.
    /// </summary>
    private async Task<IReadOnlyList<(Page Page, Anchor Anchor)>> AttachedAsync(
        Machine machine, IReadOnlyList<Installation> installed, CancellationToken cancellationToken)
    {
        var found = new List<(Page, Anchor)>();

        foreach (var anchor in Anchors(machine, installed))
        {
            foreach (var page in await pages.ListAsync(null, null, anchor, cancellationToken))
            {
                found.Add((page, anchor));
            }
        }

        return found;
    }

    private static IEnumerable<Anchor> Anchors(Machine machine, IReadOnlyList<Installation> installed)
    {
        yield return new Anchor(AnchorKind.Machine, machine.Id, machine.Key);

        foreach (var one in installed)
        {
            yield return new Anchor(AnchorKind.Installation, one.Id, one.Key);
        }
    }

    private static void Head(StringBuilder document, Machine machine, string? host)
    {
        document.Append("# ").Append(machine.Key).Append(" — ").Append(machine.Name).Append("\n\n");
        document
            .Append("A ").Append(Spelling.Of(machine.Kind)).Append(" machine, ")
            .Append(Spelling.Of(machine.Status))
            .Append(". File contents are not in this document; `ha files get PATH` reads one.\n\n");

        var facts = new (string Label, string? Value)[]
        {
            ("hostname", machine.Hostname),
            ("host", host),
            ("provider", machine.Provider),
            ("plan", machine.Plan),
            ("location", machine.Location),
            ("os", machine.Os),
            ("arch", machine.Arch is { } arch ? Spelling.Of(arch) : null),
            ("cpu", machine.Cpu),
            ("memory", machine.Memory),
            ("disk", machine.Disk),
            ("ipv4", machine.Ipv4),
            ("ipv6", machine.Ipv6),
            ("private ip", machine.PrivateIp),
            ("ssh", machine.Ssh),
            // The machine's own ports, and no installation's: what an agent
            // about to open a firewall has to know is already claimed.
            ("ports", Fields.Joined(machine.Ports, port => port.ToString())),
            ("measured", Fields.Stamp(machine.MeasuredAt)),
        };

        var said = facts.Where(fact => !string.IsNullOrEmpty(fact.Value)).ToArray();
        if (said.Length > 0)
        {
            document.Append("| | |\n|---|---|\n");
            foreach (var (label, value) in said)
            {
                document.Append("| ").Append(label).Append(" | ").Append(value).Append(" |\n");
            }
            document.Append('\n');
        }

        Description(document, machine.Description);
    }

    /// <summary>
    /// What the machine last said about itself, short and near the top — an
    /// agent about to type `docker compose up` has to read it before, not
    /// after (ADR 0015).
    /// </summary>
    /// <remarks>
    /// What is in it: when it last reported, the disks in one line, how many
    /// containers run of how many, every container that is <em>not</em>
    /// running, what listens and how far it is bound, whether a restart is
    /// waiting, and every drift. What is not: the full container table, memory
    /// and load in detail, and any older report. Those are one `ha report show`
    /// away, and they are exactly the sort of content that fills a context
    /// window without changing a decision (VISION 16).
    /// </remarks>
    private static void Reported(
        StringBuilder document,
        Report? report,
        IReadOnlyList<DriftShape> drift,
        DateTimeOffset now)
    {
        document.Append("## Reported\n\n");

        if (report is null)
        {
            document.Append(
                "This machine does not report. Nothing here is measured; every field above is what somebody "
                + "wrote down. `docs/operations.md` says how a host hands in a report.\n\n");
            return;
        }

        var age = now - report.ReceivedAt;
        document.Append("The machine last reported **").Append(Ago(age)).Append("** (")
            .Append(Fields.Stamp(report.ReceivedAt)).Append(").");

        if (age > Recent)
        {
            document.Append(" **That is not now**: what follows is what was true then, and the machine may "
                + "have been doing something else since.");
        }

        document.Append("\n\n");

        if (report.Body.Disks is { Count: > 0 } disks)
        {
            document.Append("Disks: ")
                .Append(string.Join(
                    ", ",
                    disks.Select(disk => $"{disk.Mount} {disk.Percent?.ToString(CultureInfo.InvariantCulture) ?? "?"}%")))
                .Append("\n\n");
        }

        if (report.Body.Containers is { } containers)
        {
            var running = containers.Where(one => one.State is "running").ToArray();
            var stopped = containers.Where(one => one.State is not "running").ToArray();

            document.Append("Containers: ").Append(running.Length).Append(" of ").Append(containers.Count)
                .Append(" running").Append(stopped.Length == 0 ? "." : ".").Append("\n\n");

            if (stopped.Length > 0)
            {
                document.Append("Not running: ")
                    .Append(string.Join(", ", stopped.Take(NamedContainers).Select(one => $"`{one.Name}` ({one.State})")))
                    .Append(stopped.Length > NamedContainers ? $", and {stopped.Length - NamedContainers} more" : string.Empty)
                    .Append(".\n\n");
            }
        }

        if (report.Body.Listening is { Count: > 0 } listening)
        {
            // Public first: an agent about to touch this host asks what is
            // reachable from off it before it asks anything else.
            var reachable = listening.Where(one => one.Binding is Binding.Public).ToArray();
            var loopback = listening.Where(one => one.Binding is Binding.Loopback).ToArray();

            document.Append("Listening: ").Append(Named(reachable, "public"))
                .Append(reachable.Length > 0 && loopback.Length > 0 ? "; " : string.Empty)
                .Append(Named(loopback, "loopback")).Append(".\n\n");
        }

        if (report.Body.Updates is { RebootRequired: true })
        {
            document.Append("**This machine is waiting for a restart.**\n\n");
        }

        if (report.Body.Files is { Count: > 0 } synced)
        {
            // Which directories were compared at all, because that is what the
            // absence of a file drift below means: a directory the cron was not
            // given is not checked rather than found in order (ADR 0017).
            document.Append("Files checked: ")
                .Append(string.Join(
                    ", ",
                    synced.Select(one => $"`{one.Directory}` ({one.Files.Count} of `{one.Installation}`)")))
                .Append(". A directory the cron was not given is not compared.\n\n");
        }

        foreach (var missing in report.Body.Missing)
        {
            document.Append("`").Append(missing.Section).Append("` was not determined (")
                .Append(missing.Reason).Append(").\n\n");
        }

        if (drift.Count > 0)
        {
            document.Append("**Drift** — what the record above and this report disagree about. "
                + "Which side is right is your decision; nothing here has changed the record.\n\n");

            foreach (var one in drift)
            {
                document.Append("- ").Append(one.Subject).Append(' ').Append(one.Field)
                    .Append(": the record says ").Append(one.Record ?? "nothing")
                    .Append(one.RecordAt is { } at ? $" (since {Fields.Stamp(at)})" : string.Empty)
                    .Append(", the machine reported ").Append(one.Reported ?? "nothing").Append(".\n");
            }

            document.Append('\n');
        }
    }

    /// <summary>The ports of one binding, named up to the budget and counted past it.</summary>
    private static string Named(IReadOnlyList<ListeningPort> ports, string binding)
    {
        if (ports.Count == 0)
        {
            return string.Empty;
        }

        var named = string.Join(
            ", ",
            ports.Take(NamedPorts).Select(one => $"{one.Port}/{Spelling.Of(one.Protocol)}"));

        return ports.Count > NamedPorts
            ? $"{named}, and {ports.Count - NamedPorts} more {binding}"
            : $"{named} {binding}";
    }

    /// <summary>How long ago, in the one unit that says it.</summary>
    private static string Ago(TimeSpan age) => age switch
    {
        { TotalMinutes: < 2 } => "just now",
        { TotalHours: < 2 } => $"{(int)age.TotalMinutes} minutes ago",
        { TotalDays: < 2 } => $"{(int)age.TotalHours} hours ago",
        _ => $"{(int)age.TotalDays} days ago",
    };

    private static void Installations(
        StringBuilder document,
        IReadOnlyList<Installation> installed,
        IReadOnlyList<Domain.Software> programs,
        IReadOnlyList<Deployment> recorded,
        IReadOnlyList<File> owned,
        Edge edge)
    {
        document.Append("## Installations\n\n");

        if (installed.Count == 0)
        {
            document.Append("Nothing is installed on this machine.\n\n");
            return;
        }

        var keys = programs.ToDictionary(one => one.Id, one => one.Key);

        foreach (var one in installed)
        {
            var mine = recorded.Where(deployment => deployment.InstallationId == one.Id).ToArray();

            document.Append("### ").Append(one.Key).Append(" — ").Append(one.Name).Append("\n\n");
            document
                .Append(keys.GetValueOrDefault(one.SoftwareId, "?"))
                .Append(' ').Append(Derived.Version(mine) ?? "at no recorded version")
                .Append(" · ").Append(Spelling.Of(one.Environment))
                .Append(" · ").Append(Spelling.Of(one.Role))
                .Append(" · ").Append(Spelling.Of(one.Status))
                .Append("\nbackup: ").Append(Spelling.Of(one.Backup))
                .Append(" · monitoring: ").Append(Spelling.Of(one.Monitoring))
                .Append(" · logging: ").Append(Spelling.Of(one.Logging))
                .Append('\n');

            Listed(document, "path", one.Path is { } path ? [path] : []);
            Listed(document, "data", one.Data is { } data ? [data] : []);
            Listed(document, "ports", [.. one.Ports.Select(port => port.ToString())]);
            Listed(document, "urls", one.Urls);
            Listed(document, "secrets", [.. one.Secrets.Select(secret => secret.ToString())]);

            // The two directions of the one edge, and the second is the reason
            // it exists: "needed by" is what a restart of this installation
            // takes with it.
            Listed(document, "depends on", edge.Of(one.DependsOn.Select(dependency => dependency.DependsOnId)));
            Listed(document, "needed by", edge.Of(edge.Dependents.GetValueOrDefault(one.Id) ?? []));
            document.Append('\n');

            Description(document, one.Description);

            if (Listed(
                document,
                "files",
                [.. owned
                    .Where(file => file.InstallationId == one.Id)
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .Select(file => $"{file.Path} (revision {file.Revision})")]))
            {
                document.Append('\n');
            }

            Deployments(document, mine);
        }
    }

    private static void Deployments(StringBuilder document, IReadOnlyList<Deployment> mine)
    {
        if (mine.Count == 0)
        {
            document.Append("No deployment recorded.\n\n");
            return;
        }

        var latest = Derived.InOrder(mine).Reverse().Take(RecentDeployments).ToArray();

        document.Append("Deployments, newest first:\n");
        foreach (var deployment in latest)
        {
            document
                .Append("- ").Append(deployment.Version)
                .Append(" · ").Append(deployment.At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (deployment.Ticket is { } ticket)
            {
                document.Append(" · ").Append(ticket);
            }
            if (deployment.Ref is { } reference)
            {
                document.Append(" · ").Append(reference);
            }
            document.Append('\n');
        }
        document.Append('\n');
    }

    private static void Software(StringBuilder document, IReadOnlyList<Domain.Software> programs)
    {
        if (programs.Count == 0)
        {
            return;
        }

        document.Append("## Software\n\n");

        foreach (var one in programs)
        {
            document.Append("### ").Append(one.Key).Append(" — ").Append(one.Name).Append("\n\n");
            Listed(document, "image", one.Image is { } image ? [image] : []);
            Listed(document, "homepage", one.Homepage is { } homepage ? [homepage] : []);
            Listed(document, "repository", one.Repository is { } repository ? [repository] : []);
            document.Append('\n');
            Description(document, one.Description);
        }
    }

    private static void MachineFiles(StringBuilder document, Machine machine, IReadOnlyList<File> owned)
    {
        if (owned.Count == 0)
        {
            return;
        }

        document.Append("## Files of the machine\n\n");
        foreach (var file in owned.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            document.Append("- ").Append(file.Path).Append(" (revision ").Append(file.Revision).Append(")\n");
        }
        document.Append("\n`ha files get PATH --machine ").Append(machine.Key).Append("` reads one.\n\n");
    }

    private static void Pages(StringBuilder document, IReadOnlyList<(Page Page, Anchor Anchor)> attached)
    {
        if (attached.Count == 0)
        {
            return;
        }

        document.Append("## Pages\n\n");

        foreach (var (page, anchor) in attached)
        {
            document
                .Append("### ").Append(page.Slug).Append(" — ").Append(page.Title)
                .Append("\n\n")
                .Append(Spelling.Of(page.Kind)).Append(", on ").Append(anchor).Append("\n\n");

            Description(document, page.Body);
        }
    }

    private static void Rules(StringBuilder document, IReadOnlyList<Page> rules)
    {
        if (rules.Count == 0)
        {
            return;
        }

        document.Append("## Rules of this instance\n\nDecisions that hold on every host.\n\n");

        foreach (var page in rules)
        {
            document.Append("### ").Append(page.Slug).Append(" — ").Append(page.Title).Append("\n\n");
            Description(document, page.Body);
        }
    }

    /// <summary>
    /// A label and what it holds, or nothing at all where it holds nothing.
    /// Answers whether it said anything, for the callers that follow it with a
    /// blank line only when there was a line to separate.
    /// </summary>
    private static bool Listed(StringBuilder document, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return false;
        }

        document.Append(label).Append(": ").Append(string.Join(", ", values)).Append('\n');
        return true;
    }

    /// <summary>A block of Markdown, or nothing at all where there is none.</summary>
    private static void Description(StringBuilder document, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            document.Append(text.TrimEnd()).Append("\n\n");
        }
    }
}
