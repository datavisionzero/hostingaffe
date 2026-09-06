using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The slim software every list returns: what a person reads down a column
/// (ADR 0012). The description is what would make it expensive, and the
/// description is not in it.
/// </summary>
public sealed record SoftwareSummaryShape(
    string Key,
    string Name,
    string? Homepage,
    string? Image,
    DateTimeOffset UpdatedAt);

/// <summary>The complete software: every field of VISION 7, and who touched it.</summary>
public sealed record SoftwareShape(
    string Key,
    string Name,
    string? Homepage,
    string? Repository,
    string? Image,
    string Description,
    IdentityRef CreatedBy,
    IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What a caller sends to create a software: the key, and as much of the rest
/// as is known. Everything but the key may arrive later.
/// </summary>
public sealed record CreateSoftwareRequest(
    string? Key,
    string? Name,
    string? Homepage,
    string? Repository,
    string? Image,
    string? Description)
{
    /// <inheritdoc cref="ChangeSoftwareRequest.UnknownFields"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// What a caller sends to change one: the same fields without the key, which is
/// immutable. A field left out stays as it is, and the empty string clears a
/// text field (<c>docs/api.md</c>, Software).
/// </summary>
public sealed record ChangeSoftwareRequest(
    string? Name,
    string? Homepage,
    string? Repository,
    string? Image,
    string? Description)
{
    /// <summary>
    /// Whatever the caller sent that this object does not define. A closed
    /// request object refuses it as <c>unknown-field</c> rather than ignoring
    /// what somebody meant (<c>docs/api.md</c>, Conventions) — which is also
    /// what makes the immutable key immutable, and what refuses a
    /// <c>version</c>: a software has none.
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>Turns software rows into the two shapes, resolving identities once for the whole list.</summary>
public sealed class SoftwareAssembler(IIdentities identities)
{
    public static SoftwareSummaryShape Summary(Software software)
    {
        ArgumentNullException.ThrowIfNull(software);

        return new SoftwareSummaryShape(
            software.Key,
            software.Name,
            software.Homepage,
            software.Image,
            software.UpdatedAt);
    }

    public static IReadOnlyList<SoftwareSummaryShape> Summaries(IReadOnlyList<Software> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Select(Summary)];
    }

    public async Task<SoftwareShape> CompleteAsync(Software software, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(software);

        var people = await identities.FindManyAsync([software.CreatedBy, software.UpdatedBy], cancellationToken);

        return new SoftwareShape(
            software.Key,
            software.Name,
            software.Homepage,
            software.Repository,
            software.Image,
            software.Description,
            IdentityRef.Of(people[software.CreatedBy]),
            IdentityRef.Of(people[software.UpdatedBy]),
            software.CreatedAt,
            software.UpdatedAt);
    }
}

/// <summary>The lookup every software act starts with: the key.</summary>
public static class SoftwareLookup
{
    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public static async Task<Software> LiveAsync(
        this ISoftware software, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var row = await software.AnyAsync(key, cancellationToken);

        return row.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Software {row.Key} is deleted and can be restored until at least {row.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = row.DeletedAt.Value + settings.DeletionGrace })
            : row;
    }

    public static async Task<Software> AnyAsync(
        this ISoftware software, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(software);

        // An address that is not a key names nothing, and says so as
        // `not-found` rather than as `validation`: it arrived in the path.
        var normalized = key?.Trim() ?? string.Empty;

        return (Key.IsValid(normalized) ? await software.FindAnyAsync(normalized, cancellationToken) : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No software {normalized}.");
    }
}

/// <summary>
/// Every live software, by key, slim. Not paginated: one instance holds one
/// team's infrastructure (VISION 9), and that list is read in one screen.
/// </summary>
public sealed class ListSoftware(ISoftware software)
{
    public async Task<IReadOnlyList<SoftwareSummaryShape>> ExecuteAsync(CancellationToken cancellationToken) =>
        SoftwareAssembler.Summaries(await software.ListAsync(cancellationToken));
}

public sealed class ReadSoftware(ISoftware software, SoftwareAssembler assembler, InstanceSettings settings)
{
    public async Task<SoftwareShape> ExecuteAsync(string key, CancellationToken cancellationToken) =>
        await assembler.CompleteAsync(await software.LiveAsync(key, settings, cancellationToken), cancellationToken);
}

/// <summary>The history of a software: who, when, which field, from what to what, oldest first.</summary>
public sealed class ReadSoftwareHistory(
    ISoftware software, IIdentities identities, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(string key, CancellationToken cancellationToken)
    {
        var row = await software.LiveAsync(key, settings, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.Software, row.Id, cancellationToken);

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

/// <summary>A software, in one transaction, with its birth in the history.</summary>
public sealed class CreateSoftware(
    ICallerIdentity callerIdentity,
    ISoftware software,
    IKeys keys,
    IHistory history,
    ITransactions transactions,
    SoftwareAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<SoftwareShape> ExecuteAsync(
        CreateSoftwareRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SoftwareWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var key = Validated.Field("key", () => Key.Normalize(request.Key ?? string.Empty));

        await SoftwareWrites.TakenAsync(software, keys, key, settings, cancellationToken);

        var edit = new SoftwareEdit
        {
            Homepage = request.Homepage,
            Repository = request.Repository,
            Image = request.Image,
            Description = request.Description,
        };

        var created = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var row = Validated.Field("name", () => Software.Create(key, request.Name, caller.Id, now));

            SoftwareWrites.Apply(row, edit, caller.Id, now);

            software.Add(row);
            keys.Assign(Keyed.Software, key);
            history.Add(HistoryEntry.OnSoftware(row.Id, caller.Id, now, HistoryField.Created, note: said));

            await software.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(created, cancellationToken);
    }
}

/// <summary>
/// Every field but the key, guarded by <c>If-Match</c> against
/// <c>updated_at</c>. What actually changed is what the software itself
/// reports, so the history cannot name a field the change did not touch.
/// </summary>
public sealed class ChangeSoftware(
    ICallerIdentity callerIdentity,
    ISoftware software,
    IHistory history,
    ITransactions transactions,
    SoftwareAssembler assembler,
    InstanceSettings settings,
    TimeProvider clock)
{
    public async Task<SoftwareShape> ExecuteAsync(
        string key, ChangeSoftwareRequest changes, string? ifMatch, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        SoftwareWrites.Closed(changes.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);

        var before = await software.LiveAsync(key, settings, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var edit = new SoftwareEdit
        {
            Name = changes.Name,
            Homepage = changes.Homepage,
            Repository = changes.Repository,
            Image = changes.Image,
            Description = changes.Description,
        };

        var changed = await transactions.RunAsync(async () =>
        {
            var row = await software.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No software {key}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"{row.Key} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();

            foreach (var change in SoftwareWrites.Apply(row, edit, caller.Id, now))
            {
                history.Add(HistoryEntry.OnSoftware(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue, said));
            }

            await software.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(changed, cancellationToken);
    }
}

internal static class SoftwareWrites
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
                    "key" => "A software's key is immutable; nothing renames it.",
                    "version" => "A software has no version; versions belong to deployments.",
                    _ => $"A software has no field {field}.",
                },
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <summary>
    /// A key already taken is refused as <c>validation</c> — taken by a row
    /// that is there, by one in its grace period, or by one the purge has
    /// removed, which the register still remembers. A deleted
    /// software's key says so rather than pretending the name is free.
    /// </summary>
    public static async Task TakenAsync(
        ISoftware software, IKeys keys, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        if (await software.FindAnyAsync(key, cancellationToken) is not { } existing)
        {
            // The row is gone, which is not the same as the key being free: a
            // key is never reused, and the register is what remembers after the
            // purge has taken the row that held it (VISION 7).
            if (await keys.AssignedAsync(Keyed.Software, key, cancellationToken))
            {
                throw Refusal.Validation(
                    "key",
                    $"The software {key} existed and was deleted for good; a key is never given out twice.");
            }

            return;
        }

        throw Refusal.Validation("key", existing.Deleted
            ? $"The software {key} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its key until it is purged."
            : $"The software {key} exists.");
    }

    /// <summary>The software applies what it was given, and a bad value is a refusal that names its field.</summary>
    public static IReadOnlyList<FieldChange> Apply(
        Software software, SoftwareEdit edit, Guid by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(software);

        try
        {
            return software.Apply(edit, by, at);
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(refusal.ParamName ?? "software", Validated.Said(refusal));
        }
    }
}
