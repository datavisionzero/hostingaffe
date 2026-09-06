using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The slim machine every list returns: what a person reads down a column
/// (ADR 0012). The description is what would make it expensive, and the
/// description is not in it.
/// </summary>
public sealed record MachineSummaryShape(
    string Key,
    string Name,
    MachineKind Kind,
    Status Status,
    string? Provider,
    string? Location,
    Arch? Arch,
    DateTimeOffset? MeasuredAt,
    DateTimeOffset UpdatedAt);

/// <summary>The complete machine: every field of VISION 7, and who touched it.</summary>
public sealed record MachineShape(
    string Key,
    string Name,
    string? Hostname,
    MachineKind Kind,
    string? Host,
    string? Provider,
    string? Plan,
    string? Location,
    string? Os,
    Arch? Arch,
    string? Cpu,
    string? Memory,
    string? Disk,
    string? Ipv4,
    string? Ipv6,
    string? PrivateIp,
    string? Ssh,
    Status Status,
    DateTimeOffset? MeasuredAt,
    string Description,
    IdentityRef CreatedBy,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What a caller sends to create a machine: the key and the kind, and as much
/// of the rest as is known. Everything but those two may arrive later.
/// </summary>
public sealed record CreateMachineRequest(
    string? Key,
    string? Name,
    string? Hostname,
    MachineKind? Kind,
    string? Host,
    string? Provider,
    string? Plan,
    string? Location,
    string? Os,
    Arch? Arch,
    string? Cpu,
    string? Memory,
    string? Disk,
    string? Ipv4,
    string? Ipv6,
    string? PrivateIp,
    string? Ssh,
    Status? Status,
    DateTimeOffset? MeasuredAt,
    string? Description)
{
    /// <inheritdoc cref="ChangeMachineRequest.UnknownFields"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// What a caller sends to change one: the same fields without the key, which is
/// immutable. A field left out stays as it is, and the empty string clears a
/// text field (<c>docs/api.md</c>, Machines).
/// </summary>
public sealed record ChangeMachineRequest(
    string? Name,
    string? Hostname,
    MachineKind? Kind,
    string? Host,
    string? Provider,
    string? Plan,
    string? Location,
    string? Os,
    Arch? Arch,
    string? Cpu,
    string? Memory,
    string? Disk,
    string? Ipv4,
    string? Ipv6,
    string? PrivateIp,
    string? Ssh,
    Status? Status,
    DateTimeOffset? MeasuredAt,
    string? Description)
{
    /// <summary>
    /// Whatever the caller sent that this object does not define. A closed
    /// request object refuses it as <c>unknown-field</c> rather than ignoring
    /// what somebody meant (<c>docs/api.md</c>, Conventions) — which is also
    /// what makes the immutable key immutable: <c>key</c> in a change body is a
    /// field the object does not have.
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>Turns machine rows into the two shapes, resolving identities and hosts once for the whole list.</summary>
public sealed class MachineAssembler(IIdentities identities, IMachines machines)
{
    public static MachineSummaryShape Summary(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return new MachineSummaryShape(
            machine.Key,
            machine.Name,
            machine.Kind,
            machine.Status,
            machine.Provider,
            machine.Location,
            machine.Arch,
            machine.MeasuredAt,
            machine.UpdatedAt);
    }

    public static IReadOnlyList<MachineSummaryShape> Summaries(IReadOnlyList<Machine> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(Summary)];
    }

    public async Task<MachineShape> CompleteAsync(Machine machine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        var people = await identities.FindManyAsync([machine.CreatedBy, machine.UpdatedBy], cancellationToken);
        var host = machine.HostId is { } id
            ? (await machines.KeysAsync([id], cancellationToken)).GetValueOrDefault(id)
            : null;

        return new MachineShape(
            machine.Key,
            machine.Name,
            machine.Hostname,
            machine.Kind,
            host,
            machine.Provider,
            machine.Plan,
            machine.Location,
            machine.Os,
            machine.Arch,
            machine.Cpu,
            machine.Memory,
            machine.Disk,
            machine.Ipv4,
            machine.Ipv6,
            machine.PrivateIp,
            machine.Ssh,
            machine.Status,
            machine.MeasuredAt,
            machine.Description,
            IdentityRef.Of(people[machine.CreatedBy]),
            IdentityRef.Of(people[machine.UpdatedBy]),
            machine.CreatedAt,
            machine.UpdatedAt);
    }
}

/// <summary>The lookup every machine act starts with: the key.</summary>
public static class MachineLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<Machine> LiveAsync(
        this IMachines machines, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var machine = await machines.AnyAsync(key, cancellationToken);

        return machine.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Machine {machine.Key} is deleted and can be restored until at least {machine.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = machine.DeletedAt.Value + settings.DeletionGrace })
            : machine;
    }

    public static async Task<Machine> AnyAsync(this IMachines machines, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machines);

        // An address that is not a key names nothing, and says so as
        // `not-found` rather than as `validation`: it arrived in the path.
        var normalized = key?.Trim() ?? string.Empty;

        return (Key.IsValid(normalized) ? await machines.FindAnyAsync(normalized, cancellationToken) : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No machine {normalized}.");
    }
}

/// <summary>
/// Every live machine, by key, slim. Not paginated: one instance holds one
/// team's infrastructure (VISION 9), and that list is read in one screen.
/// </summary>
/// <remarks>
/// Retired machines are not in it. Retiring is the normal end and keeps
/// everything, but a default list is what is still there; <c>retired=true</c>
/// puts them back, and <c>status=retired</c> asks for exactly them.
/// </remarks>
public sealed class ListMachines(IMachines machines)
{
    public async Task<IReadOnlyList<MachineSummaryShape>> ExecuteAsync(
        string? status, string? kind, bool retired, CancellationToken cancellationToken) =>
        MachineAssembler.Summaries(await machines.ListAsync(
            Validated.Field("status", () => Spelling.Read<Status>(status, "status")),
            Validated.Field("kind", () => Spelling.Read<MachineKind>(kind, "kind")),
            retired,
            cancellationToken));
}

public sealed class ReadMachine(IMachines machines, MachineAssembler assembler, InstanceSettings settings)
{
    public async Task<MachineShape> ExecuteAsync(string key, CancellationToken cancellationToken) =>
        await assembler.CompleteAsync(await machines.LiveAsync(key, settings, cancellationToken), cancellationToken);
}

/// <summary>The history of a machine: who, when, which field, from what to what, oldest first.</summary>
public sealed class ReadMachineHistory(
    IMachines machines, IIdentities identities, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var machine = await machines.LiveAsync(key, settings, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.Machine, machine.Id, cancellationToken);

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

/// <summary>A machine, in one transaction, with its birth in the history.</summary>
public sealed class CreateMachine(
    ICallerIdentity callerIdentity,
    IMachines machines,
    IKeys keys,
    IHistory history,
    ITransactions transactions,
    MachineAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<MachineShape> ExecuteAsync(
        CreateMachineRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        MachineWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var key = Validated.Field("key", () => Key.Normalize(request.Key ?? string.Empty));
        var kind = request.Kind ?? throw Refusal.Validation("kind", "A machine is a vps, a dedicated, a vm or a local.");

        await MachineWrites.TakenAsync(machines, keys, key, settings, cancellationToken);

        var edit = await MachineWrites.EditAsync(
            machines,
            settings,
            kind,
            request.Host,
            hostGiven: request.Host is not null,
            new MachineEdit
            {
                Hostname = request.Hostname,
                Kind = kind,
                Provider = request.Provider,
                Plan = request.Plan,
                Location = request.Location,
                Os = request.Os,
                Arch = request.Arch,
                Cpu = request.Cpu,
                Memory = request.Memory,
                Disk = request.Disk,
                Ipv4 = request.Ipv4,
                Ipv6 = request.Ipv6,
                PrivateIp = request.PrivateIp,
                Ssh = request.Ssh,
                Status = request.Status,
                MeasuredAt = request.MeasuredAt,
                Description = request.Description,
            },
            subject: null,
            cancellationToken);

        var machine = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var created = Validated.Field("name", () => Machine.Create(key, request.Name, kind, caller.Id, now));

            MachineWrites.Apply(created, edit, caller.Id, now);

            machines.Add(created);
            keys.Assign(Keyed.Machine, key);
            history.Add(HistoryEntry.OnMachine(created.Id, caller.Id, now, HistoryField.Created, note: said));

            await machines.SaveAsync(cancellationToken);
            return created;
        }, cancellationToken);

        return await assembler.CompleteAsync(machine, cancellationToken);
    }
}

/// <summary>
/// Every field but the key, guarded by <c>If-Match</c> against
/// <c>updated_at</c>. What actually changed is what the machine itself reports,
/// so the history cannot name a field the change did not touch.
/// </summary>
public sealed class ChangeMachine(
    ICallerIdentity callerIdentity,
    IMachines machines,
    IHistory history,
    ITransactions transactions,
    MachineAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<MachineShape> ExecuteAsync(
        string key, ChangeMachineRequest changes, string? ifMatch, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        MachineWrites.Closed(changes.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var before = await machines.LiveAsync(key, settings, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var edit = await MachineWrites.EditAsync(
            machines,
            settings,
            changes.Kind ?? before.Kind,
            changes.Host,
            hostGiven: changes.Host is not null,
            new MachineEdit
            {
                Name = changes.Name,
                Hostname = changes.Hostname,
                Kind = changes.Kind,
                Provider = changes.Provider,
                Plan = changes.Plan,
                Location = changes.Location,
                Os = changes.Os,
                Arch = changes.Arch,
                Cpu = changes.Cpu,
                Memory = changes.Memory,
                Disk = changes.Disk,
                Ipv4 = changes.Ipv4,
                Ipv6 = changes.Ipv6,
                PrivateIp = changes.PrivateIp,
                Ssh = changes.Ssh,
                Status = changes.Status,
                MeasuredAt = changes.MeasuredAt,
                Description = changes.Description,
            },
            subject: before,
            cancellationToken);

        var machine = await transactions.RunAsync(async () =>
        {
            var row = await machines.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No machine {key}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{row.Key} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();

            foreach (var change in MachineWrites.Apply(row, edit, caller.Id, now))
            {
                history.Add(HistoryEntry.OnMachine(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue, said));
            }

            await machines.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(machine, cancellationToken);
    }
}

internal static class MachineWrites
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
                field == "key"
                    ? "A machine's key is immutable; nothing renames it."
                    : $"A machine has no field {field}.",
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <summary>
    /// A key already taken is refused as <c>validation</c> — taken by a row
    /// that is there, by one in its grace period, or by one the purge has
    /// removed, which the register still remembers. A deleted
    /// machine's key says so rather than pretending the name is free.
    /// </summary>
    public static async Task TakenAsync(
        IMachines machines, IKeys keys, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (await machines.FindAnyAsync(key, cancellationToken) is not { } existing)
        {
            // The row is gone, which is not the same as the key being free: a
            // key is never reused, and the register is what remembers after the
            // purge has taken the row that held it (VISION 7).
            if (await keys.AssignedAsync(Keyed.Machine, key, cancellationToken))
            {
                throw Refusal.Validation(
                    "key",
                    $"The machine {key} existed and was deleted for good; a key is never given out twice.");
            }

            return;
        }

        throw Refusal.Validation("key", existing.Deleted
            ? $"The machine {key} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its key until it is purged."
            : $"The machine {key} exists.");
    }

    /// <summary>
    /// Resolves the host and settles the two rules the machine itself cannot:
    /// only a <see cref="MachineKind.Vm"/> names one, and a chain of hosts does
    /// not close on itself.
    /// </summary>
    public static async Task<MachineEdit> EditAsync(
        IMachines machines,
        InstanceSettings settings,
        MachineKind kind,
        string? host,
        bool hostGiven,
        MachineEdit edit,
        Machine? subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (!hostGiven)
        {
            return edit;
        }

        var named = host!.Trim();
        if (named.Length == 0)
        {
            return edit with { HostGiven = true, Host = null };
        }

        if (kind is not MachineKind.Vm)
        {
            throw Refusal.Validation("host", "Only a vm runs on a machine; every other kind names no host.");
        }

        var row = await machines.LiveAsync(named, settings, cancellationToken);

        if (subject is not null)
        {
            if (row.Id == subject.Id)
            {
                throw Refusal.Validation("host", $"A machine does not run on itself; {subject.Key} cannot be its own host.");
            }

            if (await machines.RunsOnAsync(row.Id, subject.Id, cancellationToken))
            {
                throw Refusal.Validation("host", $"{row.Key} already runs on {subject.Key}; a host chain does not close on itself.");
            }
        }

        return edit with { HostGiven = true, Host = row };
    }

    /// <summary>The machine applies what it was given, and a bad value is a refusal that names its field.</summary>
    public static IReadOnlyList<FieldChange> Apply(
        Machine machine, MachineEdit edit, Guid by, DateTimeOffset at)
    {
        try
        {
            return machine.Apply(edit, by, at);
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(refusal.ParamName ?? "machine", Validated.Said(refusal));
        }
    }
}
