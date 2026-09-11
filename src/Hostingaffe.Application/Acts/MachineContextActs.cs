using System.Globalization;
using System.Text;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Pages;

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
/// <strong>File contents are not in it.</strong> They are one <c>ha files
/// get</c> away, and they are what would fill a context window.
/// </remarks>
public sealed class ReadMachineContext(
    IMachines machines,
    IInstallations installations,
    ISoftware software,
    IFiles files,
    IDeployments deployments,
    IPages pages,
    InstanceSettings settings)
{
    /// <summary>
    /// How many deployments of an installation the document carries. What ran
    /// before that is a `ha deploy list` away, and the note of any of them a
    /// `ha deploy view`.
    /// </summary>
    public const int RecentDeployments = 5;

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

        var attached = await AttachedAsync(machine, installed, cancellationToken);
        var rules = (await pages.ListAsync(null, PageKind.Decision, null, cancellationToken))
            .Where(page => !page.Attached)
            .OrderBy(page => page.Slug, StringComparer.Ordinal)
            .ToArray();

        var document = new StringBuilder();
        Head(document, machine, hostKey);
        Installations(document, installed, programs, recorded, underInstallations);
        Software(document, programs);
        MachineFiles(document, machine, underMachine);
        Pages(document, attached);
        Rules(document, rules);

        return new MachineContextShape(machine.Key, document.ToString().TrimEnd() + "\n");
    }

    private static IReadOnlyList<Installation> Ordered(IEnumerable<Installation> rows) =>
        [.. rows.OrderBy(one => one.Key, StringComparer.Ordinal)];

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

    private static void Installations(
        StringBuilder document,
        IReadOnlyList<Installation> installed,
        IReadOnlyList<Domain.Software> programs,
        IReadOnlyList<Deployment> recorded,
        IReadOnlyList<File> owned)
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
