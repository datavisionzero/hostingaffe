using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Installations;
using Hostingaffe.Domain.Machines;
using Hostingaffe.Domain.Pages;

// The word is the glossary's; the alias is what the runtime's own type forces
// (docs/codebase.md).
using Environment = Hostingaffe.Domain.Installations.Environment;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What a record holds, as one document: the shape <c>ha export</c> writes and
/// the bulk write reads, so that export and import go in a circle (VISION 6.1,
/// 14).
/// </summary>
public sealed record ImportRequest(
    IReadOnlyList<ImportMachine>? Machines,
    IReadOnlyList<ImportSoftware>? Software,
    IReadOnlyList<ImportPage>? Pages)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// A machine with everything under it.
/// </summary>
/// <remarks>
/// Its closed sets arrive as the words they are spelled with rather than as the
/// enum types, the way every filter reads one from outside. It keeps a document
/// that omits an optional set from meaning the first value of it by accident,
/// and it keeps the shared enum schemas out of this shape's nullability.
/// </remarks>
public sealed record ImportMachine(
    string? Key,
    string? Name,
    string? Hostname,
    string? Kind,
    string? Host,
    string? Provider,
    string? Plan,
    string? Location,
    string? Os,
    string? Arch,
    string? Cpu,
    string? Memory,
    string? Disk,
    string? Ipv4,
    string? Ipv6,
    string? PrivateIp,
    string? Ssh,
    string? Status,
    DateTimeOffset? MeasuredAt,
    string? Description,
    IReadOnlyList<ImportInstallation>? Installations,
    IReadOnlyList<ImportFile>? Files)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// An installation of the machine it is nested under, with its files and every
/// deployment recorded on it.
/// </summary>
public sealed record ImportInstallation(
    string? Key,
    string? Name,
    string? Machine,
    string? Software,
    string? Environment,
    string? Role,
    string? Status,
    IReadOnlyList<string>? Urls,
    IReadOnlyList<PortShape>? Ports,
    string? Path,
    IReadOnlyList<string>? Secrets,
    string? Backup,
    string? Monitoring,
    string? Logging,
    string? Description,
    IReadOnlyList<ImportDeployment>? Deployments,
    IReadOnlyList<ImportFile>? Files)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>A file at the content it is at. Its earlier revisions are the source's history.</summary>
public sealed record ImportFile(string? Path, string? Content, bool? Executable)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>A deployment as it was recorded, in the order the document lists it.</summary>
public sealed record ImportDeployment(
    string? Version,
    string? Ref,
    DateTimeOffset? At,
    string? Ticket,
    string? Note)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

public sealed record ImportSoftware(
    string? Key,
    string? Name,
    string? Homepage,
    string? Repository,
    string? Image,
    string? Description)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <param name="Path">
/// Where the page came from in the source the document was made of —
/// <c>docs/setup/README.md</c>. Read only to turn the relative <c>.md</c> links
/// in the bodies into the pages they became (ADR 0007), never stored: a page is
/// addressed by its slug and by nothing else.
/// </param>
public sealed record ImportPage(
    string? Slug,
    string? Title,
    string? Body,
    string? Kind,
    AnchorShape? AttachedTo,
    string? Path)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>What the import made, counted.</summary>
/// <param name="Links">
/// How many cross-references between the document's pages it rewrote onto the
/// slugs they became. It is what says whether the paths in the document were
/// the ones the bodies actually used.
/// </param>
public sealed record ImportedShape(
    int Machines,
    int Software,
    int Installations,
    int Deployments,
    int Files,
    int Pages,
    int Links);

/// <summary>
/// A whole host in one transaction, because documenting a host is one act and
/// not thirty commands (VISION 6.1).
/// </summary>
/// <remarks>
/// <para>
/// <strong>All or nothing.</strong> It is the ordinary acts, run inside one
/// transaction that joins them: every rule they hold still holds — the keys,
/// the closed sets, the refused paths, the history each of them writes — and a
/// refusal anywhere leaves nothing standing.
/// </para>
/// <para>
/// <strong>What only the instance writes is read past.</strong> The document is
/// an export, so it carries <c>created_by</c>, <c>updated_at</c>, the history,
/// a file's <c>revision</c>, a deployment's <c>number</c>: those are accepted
/// by name and dropped. Anything else is <c>unknown-field</c>, as everywhere.
/// </para>
/// <para>
/// <strong>A file arrives at the content it is at</strong>, as its first
/// revision. What the file said before is in the source's history, and a record
/// that invented revisions it never had would be a worse copy than one that
/// says where it began.
/// </para>
/// </remarks>
public sealed class ImportRecord(
    CreateMachine createMachine,
    ChangeMachine changeMachine,
    CreateSoftware createSoftware,
    CreateInstallation createInstallation,
    CreateFile createFile,
    RecordDeployment recordDeployment,
    CreatePage createPage,
    ITransactions transactions)
{
    public async Task<ImportedShape> ExecuteAsync(
        ImportRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Imports.Closed("document", request.UnknownFields);

        var machines = request.Machines ?? [];
        var software = request.Software ?? [];
        var pages = request.Pages ?? [];

        if (machines.Count == 0 && software.Count == 0 && pages.Count == 0)
        {
            throw Refusal.Validation("document", "There is nothing in this document to create.");
        }

        OneSlugEach(pages);

        return await transactions.RunAsync(async () =>
        {
            var made = new Counter();

            foreach (var one in software)
            {
                Imports.Closed("software", one.UnknownFields, "history");
                await createSoftware.ExecuteAsync(
                    new CreateSoftwareRequest(one.Key, one.Name, one.Homepage, one.Repository, one.Image, one.Description),
                    note,
                    cancellationToken);
                made.Software++;
            }

            // Every machine first and its host afterwards: a vm may run on a
            // machine that is further down the same document, and neither
            // order of creation would satisfy both directions.
            foreach (var one in machines)
            {
                Imports.Closed("machine", one.UnknownFields, "history");
                await createMachine.ExecuteAsync(Machine(one), note, cancellationToken);
                made.Machines++;
            }

            foreach (var one in machines.Where(m => !string.IsNullOrWhiteSpace(m.Host)))
            {
                await changeMachine.ExecuteAsync(
                    one.Key ?? string.Empty,
                    new ChangeMachineRequest(
                        null, null, null, one.Host, null, null, null, null, null,
                        null, null, null, null, null, null, null, null, null, null),
                    ifMatch: null,
                    note,
                    cancellationToken);
            }

            foreach (var machine in machines)
            {
                foreach (var file in machine.Files ?? [])
                {
                    await FileAsync(AnchorKind.Machine, machine.Key, file, note, cancellationToken);
                    made.Files++;
                }

                foreach (var installation in machine.Installations ?? [])
                {
                    Imports.Closed("installation", installation.UnknownFields, "version", "history");

                    if (installation.Machine is { } named && named != machine.Key)
                    {
                        throw Refusal.Validation(
                            "machine",
                            $"{installation.Key} says it is on {named} and is written under {machine.Key}; a document says one of the two.");
                    }

                    await createInstallation.ExecuteAsync(
                        Installation(machine.Key, installation), note, cancellationToken);
                    made.Installations++;

                    foreach (var file in installation.Files ?? [])
                    {
                        await FileAsync(AnchorKind.Installation, installation.Key, file, note, cancellationToken);
                        made.Files++;
                    }

                    foreach (var deployment in installation.Deployments ?? [])
                    {
                        Imports.Closed(
                            "deployment", deployment.UnknownFields,
                            "installation", "number", "previous", "files", "by");

                        await recordDeployment.ExecuteAsync(
                            installation.Key ?? string.Empty,
                            new RecordDeploymentRequest(
                                deployment.Version, deployment.Ref, deployment.At, deployment.Ticket, deployment.Note),
                            cancellationToken);
                        made.Deployments++;
                    }
                }
            }

            var sources = ImportLinks.Sources(pages);

            foreach (var one in pages)
            {
                Imports.Closed("page", one.UnknownFields, "author", "history");
                var (body, rewritten) = ImportLinks.Rewrite(one.Body, one.Path, sources);

                await createPage.ExecuteAsync(
                    new CreatePageRequest(
                        one.Slug,
                        one.Title,
                        body,
                        Validated.Field("kind", () => Spelling.Read<PageKind>(one.Kind, "kind")),
                        one.AttachedTo),
                    note,
                    cancellationToken);
                made.Pages++;
                made.Links += rewritten;
            }

            return made.Counted();
        }, cancellationToken);
    }

    /// <summary>
    /// Two pages of one document asking for one slug, named before anything is
    /// written.
    /// </summary>
    /// <remarks>
    /// The slug space is flat and instance-wide (ADR 0003), and a repository of
    /// Markdown is not: five directories with a <c>README.md</c> in each are
    /// ordinary there and are one name here. Left to the ordinary act, the
    /// second one refuses with the slug alone, and whoever is migrating has to
    /// find out by hand which two files it meant — so the document is read for
    /// this first, and the refusal says both.
    /// </remarks>
    /// <exception cref="Refusal"><c>validation</c> on <c>slug</c>, naming both entries.</exception>
    private static void OneSlugEach(IReadOnlyList<ImportPage> pages)
    {
        var taken = new Dictionary<string, ImportPage>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            if (page.Slug?.Trim() is not { Length: > 0 } slug)
            {
                continue;
            }

            if (taken.TryGetValue(slug, out var first))
            {
                throw Refusal.Validation(
                    "slug",
                    $"{Names(first)} and {Names(page)} both want the slug {slug}, and a slug names one page in the whole instance; one of them is given another.");
            }

            taken[slug] = page;
        }
    }

    /// <summary>What to call an entry of the document in a refusal: where it came from, or what it is called.</summary>
    private static string Names(ImportPage page) =>
        page.Path?.Trim() is { Length: > 0 } path ? path
            : page.Title?.Trim() is { Length: > 0 } title ? $"the page titled {title}"
                : "a page with neither a path nor a title";

    private async Task FileAsync(
        AnchorKind kind, string? key, ImportFile file, string? note, CancellationToken cancellationToken)
    {
        Imports.Closed("file", file.UnknownFields, "owner", "revision");
        await createFile.ExecuteAsync(
            kind,
            key ?? string.Empty,
            new CreateFileRequest(file.Path, file.Content, file.Executable),
            note,
            cancellationToken);
    }

    private static CreateMachineRequest Machine(ImportMachine one) =>
        new(one.Key, one.Name, one.Hostname,
            Validated.Field("kind", () => Spelling.Read<MachineKind>(one.Kind, "kind")),
            // The host is set in the second pass, once every machine is there.
            null,
            one.Provider, one.Plan, one.Location, one.Os,
            Validated.Field("arch", () => Spelling.Read<Arch>(one.Arch, "arch")),
            one.Cpu, one.Memory, one.Disk, one.Ipv4, one.Ipv6, one.PrivateIp, one.Ssh,
            Validated.Field("status", () => Spelling.Read<Status>(one.Status, "status")),
            one.MeasuredAt, one.Description);

    // `version` is not passed on: it is derived from the deployments, and the
    // deployments are in the document. Taking both would record the first one
    // twice.
    private static CreateInstallationRequest Installation(string? machine, ImportInstallation one) =>
        new(one.Key, one.Name, machine, one.Software,
            Validated.Field("environment", () => Spelling.Read<Environment>(one.Environment, "environment")),
            Validated.Field("role", () => Spelling.Read<Role>(one.Role, "role")),
            Validated.Field("status", () => Spelling.Read<Status>(one.Status, "status")),
            one.Urls, one.Ports, one.Path, one.Secrets,
            Validated.Field("backup", () => Spelling.Read<Backup>(one.Backup, "backup")),
            Validated.Field("monitoring", () => Spelling.Read<Monitoring>(one.Monitoring, "monitoring")),
            Validated.Field("logging", () => Spelling.Read<Logging>(one.Logging, "logging")),
            null, one.Description);

    private sealed class Counter
    {
        public int Machines;
        public int Software;
        public int Installations;
        public int Deployments;
        public int Files;
        public int Pages;
        public int Links;

        public ImportedShape Counted() =>
            new(Machines, Software, Installations, Deployments, Files, Pages, Links);
    }
}

internal static class Imports
{
    /// <summary>
    /// The fields of an export that only the instance writes. They arrive
    /// because the document <em>is</em> an export, and they are read past by
    /// name — anything the object does not define and this list does not name
    /// is <c>unknown-field</c>, as everywhere else.
    /// </summary>
    private static readonly string[] Written =
        ["created_by", "updated_by", "created_at", "updated_at"];

    /// <exception cref="Refusal"><c>unknown-field</c>, naming the field and what carried it.</exception>
    public static void Closed(string what, Dictionary<string, JsonElement>? unknown, params string[] alsoWritten)
    {
        if (unknown is null)
        {
            return;
        }

        foreach (var field in unknown.Keys)
        {
            if (Written.Contains(field, StringComparer.Ordinal)
                || alsoWritten.Contains(field, StringComparer.Ordinal))
            {
                continue;
            }

            throw new Refusal(
                RefusalCode.UnknownField,
                $"A {what} in an import has no field {field}.",
                new Dictionary<string, object?> { ["field"] = field });
        }
    }
}
