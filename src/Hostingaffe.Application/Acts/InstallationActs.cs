using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Installations;

// The model's word for whom an installation serves is `environment`
// (CONTEXT.md); `System` is implicitly imported, so the two are said apart here.
using Environment = Hostingaffe.Domain.Installations.Environment;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// One port an installation listens on, as the contract carries it:
/// <c>{ "port": 443, "protocol": "tcp", "scope": "public" }</c>.
/// </summary>
/// <remarks>
/// An object rather than the string <c>443/tcp:public</c>, because that
/// spelling is a rendering: as a field it would be the one value in the model
/// with a grammar of its own, and a generated client would see a <c>string</c>
/// it can read nothing out of. The spelling is what the CLI and the interface
/// show and take.
/// </remarks>
public sealed record PortShape(int Port, Protocol Protocol, Scope Scope)
{
    public static PortShape Of(Port port)
    {
        ArgumentNullException.ThrowIfNull(port);
        return new PortShape(port.Number, port.Protocol, port.Scope);
    }
}

/// <summary>
/// One secret an installation needs, as the contract carries it:
/// <c>{ "name": "POSTGRES_PASSWORD", "path": "/opt/compose/logaffe/.env.runtime" }</c>.
/// </summary>
/// <remarks>
/// The name is a name and never a value (VISION 7); <c>path</c> is the file on
/// the machine the value lies in, and is left out where nobody has decided yet.
/// An object rather than the spelling <c>NAME@/path</c> for the reason a port is
/// one: that spelling is what a person types and what a history row carries.
/// </remarks>
public sealed record SecretShape(string Name, string? Path)
{
    public static SecretShape Of(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return new SecretShape(secret.Name, secret.Path);
    }
}

/// <summary>
/// The slim installation every list returns: the two keys, the three closed
/// sets that say what it is, and the three decisions — because "every
/// production installation without a backup" is a column a person reads down
/// (VISION 7, ADR 0012). The description and the lists are what would make it
/// expensive, and they are not in it.
/// </summary>
public sealed record InstallationSummaryShape(
    string Key,
    string Name,
    string Machine,
    string Software,
    Environment Environment,
    Role Role,
    Status Status,
    Backup Backup,
    Monitoring Monitoring,
    Logging Logging,
    string? Version,
    DateTimeOffset UpdatedAt);

/// <summary>The complete installation: every field of VISION 7, and who touched it.</summary>
/// <remarks>
/// <see cref="DependsOn"/> and <see cref="NeededBy"/> are the one edge read from
/// its two ends. The first is written and is the installations this one needs;
/// the second is derived on read and is the installations that need it — the
/// question a person actually asks before an intervention, and never a second
/// list somebody could write into disagreement with the first (ADR 0014). Both
/// are keys in key order, one hop and no closure.
/// </remarks>
public sealed record InstallationShape(
    string Key,
    string Name,
    string Machine,
    string Software,
    Environment Environment,
    Role Role,
    Status Status,
    IReadOnlyList<string> Urls,
    IReadOnlyList<PortShape> Ports,
    string? Path,
    string? Data,
    IReadOnlyList<SecretShape> Secrets,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> NeededBy,
    Backup Backup,
    Monitoring Monitoring,
    Logging Logging,
    string? Version,
    string Description,
    IdentityRef CreatedBy,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What a caller sends to create an installation: the key, the machine, the
/// software, the environment and the role. The rest may arrive later.
/// </summary>
/// <remarks>
/// <para>
/// The five are required because none of them has a default that would be
/// true: an installation is a software on a machine, and <c>environment</c> and
/// <c>role</c> answer the two questions VISION 7 says every installation
/// answers. The three decisions do have one — <c>none</c>, <c>none</c>,
/// <c>local</c> — and it is the honest starting state: nothing decided yet is
/// no backup.
/// </para>
/// <para>
/// <see cref="Version"/> is the one field here that is not the installation's:
/// given, it records the first deployment in the same transaction, so that an
/// installation never has a version without a record of when it appeared
/// (VISION 7). Left out, there is no deployment yet and no version — which is
/// what a <c>planned</c> installation is.
/// </para>
/// </remarks>
public sealed record CreateInstallationRequest(
    string? Key,
    string? Name,
    string? Machine,
    string? Software,
    Environment? Environment,
    Role? Role,
    Status? Status,
    IReadOnlyList<string>? Urls,
    IReadOnlyList<PortShape>? Ports,
    string? Path,
    string? Data,
    IReadOnlyList<SecretShape>? Secrets,
    IReadOnlyList<string>? DependsOn,
    Backup? Backup,
    Monitoring? Monitoring,
    Logging? Logging,
    string? Version,
    string? Description)
{
    /// <inheritdoc cref="ChangeInstallationRequest.UnknownFields"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// What a caller sends to change one: the same fields without the key, which is
/// immutable. A field left out stays as it is, the empty string clears a text
/// field, and an empty list clears a list (<c>docs/api.md</c>, Installations).
/// </summary>
public sealed record ChangeInstallationRequest(
    string? Name,
    string? Machine,
    string? Software,
    Environment? Environment,
    Role? Role,
    Status? Status,
    IReadOnlyList<string>? Urls,
    IReadOnlyList<PortShape>? Ports,
    string? Path,
    string? Data,
    IReadOnlyList<SecretShape>? Secrets,
    IReadOnlyList<string>? DependsOn,
    Backup? Backup,
    Monitoring? Monitoring,
    Logging? Logging,
    string? Description)
{
    /// <summary>
    /// Whatever the caller sent that this object does not define. A closed
    /// request object refuses it as <c>unknown-field</c> rather than ignoring
    /// what somebody meant (<c>docs/api.md</c>, Conventions) — which is what
    /// makes the key immutable, and what refuses <c>version</c> and
    /// <c>needed_by</c>: both are derived, the first from the deployments and
    /// the second from the dependencies read the other way.
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// Turns installation rows into the two shapes, resolving identities, machines
/// and software once for the whole list rather than once per row.
/// </summary>
/// <remarks>
/// The <c>version</c> is derived here and nowhere else, and it is
/// <see cref="Derived"/> that says what it means: the version of the latest
/// deployment <em>by <c>at</c></em>. A list that computed it by the order of
/// recording would move the present every time somebody backfilled the past.
/// </remarks>
public sealed class InstallationAssembler(
    IIdentities identities,
    IMachines machines,
    ISoftware software,
    IInstallations installations,
    IDeployments deployments)
{
    public async Task<IReadOnlyList<InstallationSummaryShape>> SummariesAsync(
        IReadOnlyList<Installation> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var (machineKeys, softwareKeys) = await KeysAsync(rows, cancellationToken);
        var versions = await VersionsAsync(rows, cancellationToken);

        return
        [
            .. rows.Select(row => new InstallationSummaryShape(
                row.Key,
                row.Name,
                machineKeys[row.MachineId],
                softwareKeys[row.SoftwareId],
                row.Environment,
                row.Role,
                row.Status,
                row.Backup,
                row.Monitoring,
                row.Logging,
                versions.GetValueOrDefault(row.Id),
                row.UpdatedAt)),
        ];
    }

    public async Task<InstallationShape> CompleteAsync(
        Installation installation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var people = await identities.FindManyAsync(
            [installation.CreatedBy, installation.UpdatedBy], cancellationToken);
        var (machineKeys, softwareKeys) = await KeysAsync([installation], cancellationToken);
        var versions = await VersionsAsync([installation], cancellationToken);
        var (dependsOn, neededBy) = await EdgeAsync(installation, cancellationToken);

        return new InstallationShape(
            installation.Key,
            installation.Name,
            machineKeys[installation.MachineId],
            softwareKeys[installation.SoftwareId],
            installation.Environment,
            installation.Role,
            installation.Status,
            installation.Urls,
            [.. installation.Ports.Select(PortShape.Of)],
            installation.Path,
            installation.Data,
            [.. installation.Secrets.Select(SecretShape.Of)],
            dependsOn,
            neededBy,
            installation.Backup,
            installation.Monitoring,
            installation.Logging,
            versions.GetValueOrDefault(installation.Id),
            installation.Description,
            IdentityRef.Of(people[installation.CreatedBy]),
            IdentityRef.Of(people[installation.UpdatedBy]),
            installation.CreatedAt,
            installation.UpdatedAt);
    }

    /// <summary>
    /// The one edge from both ends, in key order: what this installation needs,
    /// and what needs it. Two reads and no join through the shape — the second
    /// is the same rows asked for by their target, which is what the index on
    /// <c>depends_on_id</c> is for (ADR 0014).
    /// </summary>
    /// <remarks>
    /// A key is resolved rather than stored beside the id, so a dependency that
    /// the purge has not reached yet still reads as the key it is. What the
    /// purge has taken is gone from both ends, because it takes the edges before
    /// it takes the row.
    /// </remarks>
    private async Task<(IReadOnlyList<string> DependsOn, IReadOnlyList<string> NeededBy)> EdgeAsync(
        Installation installation, CancellationToken cancellationToken)
    {
        var dependents = (await installations.DependentsAsync([installation.Id], cancellationToken))
            .GetValueOrDefault(installation.Id) ?? [];

        var keys = await installations.KeysAsync(
            installation.DependsOn.Select(one => one.DependsOnId).Concat(dependents), cancellationToken);

        return (Named(installation.DependsOn.Select(one => one.DependsOnId)), Named(dependents));

        IReadOnlyList<string> Named(IEnumerable<Guid> ids) =>
        [
            .. ids.Select(id => keys.GetValueOrDefault(id))
                .OfType<string>()
                .OrderBy(key => key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The version each of these installations runs, by the one rule that says
    /// what "latest" means.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(
        IReadOnlyList<Installation> rows, CancellationToken cancellationToken)
    {
        var all = await deployments.ListAsync(rows.Select(row => row.Id), cancellationToken);

        return all
            .GroupBy(one => one.InstallationId)
            .Select(group => (group.Key, Version: Derived.Version(group)))
            .Where(found => found.Version is not null)
            .ToDictionary(found => found.Key, found => found.Version!);
    }

    private async Task<(IReadOnlyDictionary<Guid, string> Machines, IReadOnlyDictionary<Guid, string> Software)> KeysAsync(
        IReadOnlyList<Installation> rows, CancellationToken cancellationToken) =>
        (await machines.KeysAsync(rows.Select(row => row.MachineId), cancellationToken),
         await software.KeysAsync(rows.Select(row => row.SoftwareId), cancellationToken));
}

/// <summary>The lookup every installation act starts with: the key.</summary>
public static class InstallationLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<Installation> LiveAsync(
        this IInstallations installations, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var installation = await installations.AnyAsync(key, cancellationToken);

        return installation.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Installation {installation.Key} is deleted and can be restored until at least {installation.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = installation.DeletedAt.Value + settings.DeletionGrace })
            : installation;
    }

    public static async Task<Installation> AnyAsync(
        this IInstallations installations, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installations);

        // An address that is not a key names nothing, and says so as
        // `not-found` rather than as `validation`: it arrived in the path.
        var normalized = key?.Trim() ?? string.Empty;

        return (Key.IsValid(normalized) ? await installations.FindAnyAsync(normalized, cancellationToken) : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No installation {normalized}.");
    }
}

/// <summary>
/// Every live installation the filters admit, by key, slim. The filters are the
/// two keys and one value of each closed set, which is what makes "every
/// production installation without a backup" one call.
/// </summary>
public sealed class ListInstallations(
    IInstallations installations,
    IMachines machines,
    ISoftware software,
    InstallationAssembler assembler,
    InstanceSettings settings)
{
    public async Task<IReadOnlyList<InstallationSummaryShape>> ExecuteAsync(
        string? machine,
        string? softwareKey,
        string? environment,
        string? role,
        string? status,
        string? backup,
        string? monitoring,
        string? logging,
        bool retired,
        CancellationToken cancellationToken)
    {
        var filter = new InstallationFilter
        {
            Retired = retired,
            MachineId = string.IsNullOrWhiteSpace(machine)
                ? null
                : (await Validated.FieldAsync("machine", () => machines.LiveAsync(machine, settings, cancellationToken))).Id,
            SoftwareId = string.IsNullOrWhiteSpace(softwareKey)
                ? null
                : (await Validated.FieldAsync("software", () => software.LiveAsync(softwareKey, settings, cancellationToken))).Id,
            Environment = Validated.Field("environment", () => Spelling.Read<Environment>(environment, "environment")),
            Role = Validated.Field("role", () => Spelling.Read<Role>(role, "role")),
            Status = Validated.Field("status", () => Spelling.Read<Status>(status, "status")),
            Backup = Validated.Field("backup", () => Spelling.Read<Backup>(backup, "backup")),
            Monitoring = Validated.Field("monitoring", () => Spelling.Read<Monitoring>(monitoring, "monitoring")),
            Logging = Validated.Field("logging", () => Spelling.Read<Logging>(logging, "logging")),
        };

        return await assembler.SummariesAsync(
            await installations.ListAsync(filter, cancellationToken), cancellationToken);
    }
}

public sealed class ReadInstallation(
    IInstallations installations, InstallationAssembler assembler, InstanceSettings settings)
{
    public async Task<InstallationShape> ExecuteAsync(string key, CancellationToken cancellationToken) =>
        await assembler.CompleteAsync(
            await installations.LiveAsync(key, settings, cancellationToken), cancellationToken);
}

/// <summary>The history of an installation: who, when, which field, from what to what, oldest first.</summary>
public sealed class ReadInstallationHistory(
    IInstallations installations, IIdentities identities, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var installation = await installations.LiveAsync(key, settings, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.Installation, installation.Id, cancellationToken);

        var people = await identities.FindManyAsync(
            entries.Select(entry => entry.ActorId).Distinct(), cancellationToken);

        return
        [
            .. entries.Select(entry => new HistoryEntryShape(
                entry.Id,
                IdentityRef.Of(people[entry.ActorId]),
                entry.At,
                entry.Field,
                entry.OldValue,
                entry.NewValue,
                entry.Note)),
        ];
    }
}

/// <summary>
/// An installation, in one transaction, with its birth in the history — and
/// with its first deployment where the caller gave a version, so that an
/// installation never has one without a record of when it appeared (VISION 7).
/// </summary>
public sealed class CreateInstallation(
    ICallerIdentity callerIdentity,
    IInstallations installations,
    IMachines machines,
    ISoftware software,
    IDeployments deployments,
    IKeys keys,
    IHistory history,
    ITransactions transactions,
    InstallationAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<InstallationShape> ExecuteAsync(
        CreateInstallationRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        InstallationWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var key = Validated.Field("key", () => Key.Normalize(request.Key ?? string.Empty));

        var machine = await InstallationWrites.MachineAsync(machines, request.Machine, settings, cancellationToken)
            ?? throw Refusal.Validation("machine", "An installation is on a machine, and the machine is named by key.");
        var installed = await InstallationWrites.SoftwareAsync(software, request.Software, settings, cancellationToken)
            ?? throw Refusal.Validation("software", "An installation is an installation of a software, named by key.");

        var environment = request.Environment
            ?? throw Refusal.Validation("environment", "An installation serves production, staging or development.");
        var role = request.Role
            ?? throw Refusal.Validation("role", "An installation is an application or a platform.");

        await InstallationWrites.TakenAsync(installations, keys, key, settings, cancellationToken);

        var edit = InstallationWrites.Edit(request) with
        {
            DependsOn = await InstallationWrites.DependsOnAsync(
                installations, request.DependsOn, cancellationToken),
        };

        var created = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var row = Validated.Field(
                "name",
                () => Installation.Create(key, request.Name, machine.Id, installed.Id, environment, role, caller.Id, now));

            InstallationWrites.Apply(row, edit, caller.Id, now);

            installations.Add(row);
            keys.Assign(Keyed.Installation, key);
            history.Add(HistoryEntry.OnInstallation(row.Id, caller.Id, now, HistoryField.Created, note: said));

            // The first deployment is part of the same act, not a second call a
            // caller could forget: a version with no record of when it appeared
            // is exactly what VISION 7 rules out. No version given means no
            // deployment yet, which is what a planned installation is.
            if (!string.IsNullOrWhiteSpace(request.Version))
            {
                var first = Validated.Field(
                    "version",
                    () => Domain.Deployments.Deployment.Record(row.Id, 1, request.Version, now, caller.Id, now));

                deployments.Add(first);
                history.Add(HistoryEntry.OnDeployment(
                    first.Id, caller.Id, now, HistoryField.Created, null, first.Version, said));
            }

            await installations.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(created, cancellationToken);
    }
}

/// <summary>
/// Every field but the key, guarded by <c>If-Match</c> against
/// <c>updated_at</c>. What actually changed is what the installation itself
/// reports, so the history cannot name a field the change did not touch.
/// </summary>
public sealed class ChangeInstallation(
    ICallerIdentity callerIdentity,
    IInstallations installations,
    IMachines machines,
    ISoftware software,
    IHistory history,
    ITransactions transactions,
    InstallationAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<InstallationShape> ExecuteAsync(
        string key,
        ChangeInstallationRequest changes,
        string? ifMatch,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        InstallationWrites.Closed(changes.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var before = await installations.LiveAsync(key, settings, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var edit = InstallationWrites.Edit(changes) with
        {
            Machine = await InstallationWrites.MachineAsync(machines, changes.Machine, settings, cancellationToken),
            Software = await InstallationWrites.SoftwareAsync(software, changes.Software, settings, cancellationToken),
            DependsOn = await InstallationWrites.DependsOnAsync(installations, changes.DependsOn, cancellationToken),
        };

        var changed = await transactions.RunAsync(async () =>
        {
            var row = await installations.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No installation {key}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{row.Key} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();

            foreach (var change in InstallationWrites.Apply(row, edit, caller.Id, now))
            {
                history.Add(HistoryEntry.OnInstallation(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue, said));
            }

            await installations.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(changed, cancellationToken);
    }
}

internal static class InstallationWrites
{
    /// <summary>
    /// A field the object does not define is <c>unknown-field</c>, never
    /// silently ignored (<c>docs/api.md</c>, Conventions).
    /// </summary>
    public static void Closed(Dictionary<string, JsonElement>? unknown)
    {
        if (unknown?.Keys.FirstOrDefault() is { } field)
        {
            throw new Refusal(
                RefusalCode.UnknownField,
                field switch
                {
                    "key" => "An installation's key is immutable; nothing renames it.",
                    "version" => "An installation's version is derived from its deployments and is not written here.",
                    "needed_by" => "What needs an installation is read from the depends_on of the installations that name it, and is not written here.",
                    _ => $"An installation has no field {field}.",
                },
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <summary>
    /// A key already taken is refused as <c>validation</c> — taken by a row
    /// that is there, by one in its grace period, or by one the purge has
    /// removed, which the register still remembers. A deleted
    /// installation's key says so rather than pretending the name is free.
    /// </summary>
    public static async Task TakenAsync(
        IInstallations installations, IKeys keys, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (await installations.FindAnyAsync(key, cancellationToken) is not { } existing)
        {
            // The row is gone, which is not the same as the key being free: a
            // key is never reused, and the register is what remembers after the
            // purge has taken the row that held it (VISION 7).
            if (await keys.AssignedAsync(Keyed.Installation, key, cancellationToken))
            {
                throw Refusal.Validation(
                    "key",
                    $"The installation {key} existed and was deleted for good; a key is never given out twice.");
            }

            return;
        }

        throw Refusal.Validation("key", existing.Deleted
            ? $"The installation {key} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its key until it is purged."
            : $"The installation {key} exists.");
    }

    /// <summary>
    /// The machine a caller named, or nothing where they named none. A machine
    /// that does not exist is <c>validation</c> on the field it arrived in, not
    /// a <c>not-found</c> about an address nobody asked for.
    /// </summary>
    public static async Task<Domain.Machines.Machine?> MachineAsync(
        IMachines machines, string? key, InstanceSettings settings, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : await Validated.FieldAsync("machine", () => machines.LiveAsync(key, settings, cancellationToken));

    /// <inheritdoc cref="MachineAsync"/>
    public static async Task<Software?> SoftwareAsync(
        ISoftware software, string? key, InstanceSettings settings, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : await Validated.FieldAsync("software", () => software.LiveAsync(key, settings, cancellationToken));

    /// <summary>
    /// The installations a caller named by key, as rows — <c>null</c> where they
    /// named none, which leaves the list alone, and empty where they sent an
    /// empty list, which clears it.
    /// </summary>
    /// <remarks>
    /// One read for the whole list, and a key nothing live answers to is
    /// <c>validation</c> on the field it arrived in rather than a
    /// <c>not-found</c> about an address nobody asked for — the rule the machine
    /// and the software of an installation follow. A deleted installation is not
    /// one to depend on: it is on its way out, and the record should not grow a
    /// new reason to keep it.
    /// </remarks>
    public static async Task<IReadOnlyList<Installation>?> DependsOnAsync(
        IInstallations installations, IReadOnlyList<string>? keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installations);

        if (keys is null)
        {
            return null;
        }

        var wanted = keys.Select(one => one?.Trim() ?? string.Empty).ToArray();
        var found = await installations.LiveByKeysAsync(wanted.Where(Key.IsValid), cancellationToken);
        var rows = found.ToDictionary(one => one.Key, StringComparer.Ordinal);

        return
        [
            .. wanted.Select(one => rows.TryGetValue(one, out var row)
                ? row
                : throw Refusal.Validation(
                    "depends_on",
                    one.Length == 0
                        ? "A depends_on entry is the key of an installation."
                        : $"No installation {one}; depends_on names installations by key.")),
        ];
    }

    public static InstallationEdit Edit(CreateInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new InstallationEdit
        {
            Status = request.Status,
            Urls = request.Urls,
            Ports = Ports(request.Ports),
            Path = request.Path,
            Data = request.Data,
            Secrets = Secrets(request.Secrets),
            Backup = request.Backup,
            Monitoring = request.Monitoring,
            Logging = request.Logging,
            Description = request.Description,
        };
    }

    public static InstallationEdit Edit(ChangeInstallationRequest changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return new InstallationEdit
        {
            Name = changes.Name,
            Environment = changes.Environment,
            Role = changes.Role,
            Status = changes.Status,
            Urls = changes.Urls,
            Ports = Ports(changes.Ports),
            Path = changes.Path,
            Data = changes.Data,
            Secrets = Secrets(changes.Secrets),
            Backup = changes.Backup,
            Monitoring = changes.Monitoring,
            Logging = changes.Logging,
            Description = changes.Description,
        };
    }

    /// <summary>The installation applies what it was given, and a bad value is a refusal that names its field.</summary>
    public static IReadOnlyList<FieldChange> Apply(
        Installation installation, InstallationEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(installation);

        try
        {
            return installation.Apply(edit, by, at);
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(refusal.ParamName ?? "installation", Validated.Said(refusal));
        }
    }

    private static IReadOnlyList<Port>? Ports(IReadOnlyList<PortShape>? given) =>
        given is null
            ? null
            : [.. given.Select(port => Validated.Field(
                "ports", () => Port.Of(port.Port, port.Protocol, port.Scope)))];

    private static IReadOnlyList<Secret>? Secrets(IReadOnlyList<SecretShape>? given) =>
        given is null
            ? null
            : [.. given.Select(secret => Validated.Field(
                "secrets", () => Secret.Of(secret.Name, secret.Path)))];
}
