using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Providers;

namespace Hostingaffe.Application.Acts;

public sealed record ProviderSummaryShape(string Key, string Name, DateTimeOffset UpdatedAt);

public sealed record ProviderShape(
    string Key, string Name, string Description,
    IdentityRef CreatedBy, IdentityRef UpdatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateProviderRequest(string? Key, string? Name, string? Description)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

public sealed record ChangeProviderRequest(string? Name, string? Description)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

public sealed class ProviderAssembler(IIdentities identities)
{
    public static ProviderSummaryShape Summary(Provider provider) =>
        new(provider.Key, provider.Name, provider.UpdatedAt);

    public async Task<ProviderShape> CompleteAsync(Provider provider, CancellationToken cancellationToken)
    {
        var people = await identities.FindManyAsync([provider.CreatedBy, provider.UpdatedBy], cancellationToken);
        return new ProviderShape(
            provider.Key, provider.Name, provider.Description,
            IdentityRef.Of(people[provider.CreatedBy]),
            IdentityRef.Of(people[provider.UpdatedBy]),
            provider.CreatedAt, provider.UpdatedAt);
    }
}

public static class ProviderLookup
{
    public static async Task<Provider> AnyAsync(
        this IProviders providers, string key, CancellationToken cancellationToken)
    {
        var normalized = key?.Trim() ?? string.Empty;
        return (Key.IsValid(normalized)
                ? await providers.FindAnyAsync(normalized, cancellationToken)
                : null)
            ?? throw new Refusal(RefusalCode.NotFound, $"No provider {normalized}.");
    }

    public static async Task<Provider> LiveAsync(
        this IProviders providers, string key, InstanceSettings settings, CancellationToken cancellationToken)
    {
        var provider = await providers.AnyAsync(key, cancellationToken);
        return provider.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Provider {provider.Key} is deleted and can be restored until at least {provider.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = provider.DeletedAt.Value + settings.DeletionGrace })
            : provider;
    }
}

public sealed class ListProviders(IProviders providers)
{
    public async Task<IReadOnlyList<ProviderSummaryShape>> ExecuteAsync(CancellationToken cancellationToken) =>
        [.. (await providers.ListAsync(cancellationToken)).Select(ProviderAssembler.Summary)];
}

public sealed class ReadProvider(IProviders providers, ProviderAssembler assembler, InstanceSettings settings)
{
    public async Task<ProviderShape> ExecuteAsync(string key, CancellationToken cancellationToken) =>
        await assembler.CompleteAsync(await providers.LiveAsync(key, settings, cancellationToken), cancellationToken);
}

public sealed class ReadProviderHistory(
    IProviders providers, IIdentities identities, IHistory history, InstanceSettings settings)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(
        string key, CancellationToken cancellationToken)
    {
        var provider = await providers.LiveAsync(key, settings, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.Provider, provider.Id, cancellationToken);
        var people = await identities.FindManyAsync(entries.Select(entry => entry.ActorId).Distinct(), cancellationToken);
        return [.. entries.Select(entry => new HistoryEntryShape(
            entry.Id, IdentityRef.Of(people[entry.ActorId]), entry.At,
            entry.Field, entry.OldValue, entry.NewValue, entry.Note))];
    }
}

public sealed class CreateProvider(
    ICallerIdentity callerIdentity, IProviders providers, IKeys keys,
    IHistory history, ITransactions transactions, ProviderAssembler assembler,
    InstanceSettings settings, TimeProvider clock)
{
    public async Task<ProviderShape> ExecuteAsync(
        CreateProviderRequest request, string? note, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProviderWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var key = Validated.Field("key", () => Key.Normalize(request.Key ?? string.Empty));
        await ProviderWrites.TakenAsync(providers, keys, key, settings, cancellationToken);

        var created = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var row = Validated.Field("name", () => Provider.Create(key, request.Name, caller.Id, now));
            ProviderWrites.Apply(row, new ProviderEdit(Description: request.Description), caller.Id, now);
            providers.Add(row);
            keys.Assign(Keyed.Provider, key);
            history.Add(HistoryEntry.OnProvider(row.Id, caller.Id, now, HistoryField.Created, note: said));
            await providers.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);
        return await assembler.CompleteAsync(created, cancellationToken);
    }
}

public sealed class ChangeProvider(
    ICallerIdentity callerIdentity, IProviders providers, IHistory history,
    ITransactions transactions, ProviderAssembler assembler,
    InstanceSettings settings, TimeProvider clock)
{
    public async Task<ProviderShape> ExecuteAsync(
        string key, ChangeProviderRequest changes, string? ifMatch, string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ProviderWrites.Closed(changes.UnknownFields);
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await providers.LiveAsync(key, settings, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var changed = await transactions.RunAsync(async () =>
        {
            var row = await providers.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No provider {key}.");
            if (row.Deleted)
                throw new Refusal(RefusalCode.Deleted, $"Provider {row.Key} is deleted.");
            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(RefusalCode.Stale,
                    $"{row.Key} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?> { ["current"] = await assembler.CompleteAsync(row, cancellationToken) });
            }

            var now = clock.GetUtcNow();
            foreach (var change in ProviderWrites.Apply(
                         row, new ProviderEdit(changes.Name, changes.Description), caller.Id, now))
            {
                history.Add(HistoryEntry.OnProvider(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue, said));
            }

            await providers.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);
        return await assembler.CompleteAsync(changed, cancellationToken);
    }
}

public sealed class MoveProvider(
    ICallerIdentity callerIdentity, IProviders providers, IHistory history,
    ITransactions transactions, ProviderAssembler assembler,
    InstanceSettings settings, TimeProvider clock)
{
    public async Task DeleteAsync(string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await providers.LiveAsync(key, settings, cancellationToken);
        await transactions.RunAsync(async () =>
        {
            var row = await providers.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No provider {key}.");
            if (row.Deleted)
                throw new Refusal(RefusalCode.Deleted, $"Provider {row.Key} is deleted.");
            if (await providers.InUseAsync(row.Key, cancellationToken))
            {
                throw new Refusal(RefusalCode.Transition,
                    $"Provider {row.Key} still has machines; it cannot be deleted while they refer to it.");
            }

            var now = clock.GetUtcNow();
            row.Delete(caller.Id, now);
            history.Add(HistoryEntry.OnProvider(row.Id, caller.Id, now, HistoryField.Deleted, note: said));
            await providers.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<ProviderShape> RestoreAsync(
        string key, string? note, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller;
        var said = Validated.Note(note);
        var before = await providers.AnyAsync(key, cancellationToken);
        if (!before.Deleted)
            throw new Refusal(RefusalCode.Transition, $"Provider {before.Key} is not deleted.");

        var restored = await transactions.RunAsync(async () =>
        {
            var row = await providers.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"No provider {key}.");
            if (!row.Deleted)
                throw new Refusal(RefusalCode.Transition, $"Provider {row.Key} is not deleted.");
            var now = clock.GetUtcNow();
            row.Restore();
            history.Add(HistoryEntry.OnProvider(row.Id, caller.Id, now, HistoryField.Restored, note: said));
            await providers.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);
        return await assembler.CompleteAsync(restored, cancellationToken);
    }
}

internal static class ProviderWrites
{
    public static void Closed(Dictionary<string, JsonElement>? unknown)
    {
        if (unknown?.Keys.FirstOrDefault() is { } field)
        {
            throw new Refusal(RefusalCode.UnknownField,
                field == "key" ? "A provider's key is immutable; nothing renames it."
                    : $"A provider has no field {field}.",
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    public static async Task TakenAsync(
        IProviders providers, IKeys keys, string key, InstanceSettings settings,
        CancellationToken cancellationToken)
    {
        if (await providers.FindAnyAsync(key, cancellationToken) is not { } existing)
        {
            if (await keys.AssignedAsync(Keyed.Provider, key, cancellationToken))
                throw Refusal.Validation("key", $"The provider {key} existed and was deleted for good; a key is never given out twice.");
            return;
        }

        throw Refusal.Validation("key", existing.Deleted
            ? $"The provider {key} is deleted and can be restored until at least {existing.DeletedAt!.Value + settings.DeletionGrace:u}; a new one cannot take its key."
            : $"The provider {key} exists.");
    }

    public static IReadOnlyList<FieldChange> Apply(
        Provider provider, ProviderEdit edit, Guid by, DateTimeOffset at)
    {
        try { return provider.Apply(edit, by, at); }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(refusal.ParamName ?? "provider", Validated.Said(refusal));
        }
    }
}
