using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Deployments;
using Hostingaffe.Domain.History;
using Hostingaffe.Domain.Installations;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// One file of the installation as the deployment found it: the path, and the
/// revision that was current when the version went live.
/// </summary>
public sealed record DeploymentFileShape(string Path, int Revision)
{
    public static DeploymentFileShape Of(DeployedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new DeploymentFileShape(file.Path, file.Revision);
    }
}

/// <summary>The slim deployment every list returns: what a person reads down a column.</summary>
public sealed record DeploymentSummaryShape(
    int Number,
    string Version,
    string? Previous,
    string? Ref,
    DateTimeOffset At,
    IdentityRef By,
    string? Ticket,
    DateTimeOffset UpdatedAt);

/// <summary>The complete deployment: every field of VISION 7, derived ones included.</summary>
public sealed record DeploymentShape(
    string Installation,
    int Number,
    string Version,
    string? Previous,
    string? Ref,
    IReadOnlyList<DeploymentFileShape> Files,
    DateTimeOffset At,
    IdentityRef By,
    string? Ticket,
    string Note,
    DateTimeOffset CreatedAt,
    IdentityRef UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What a caller sends to record one. Only <c>version</c> is required: a
/// deployment is a version that ran, and that is what it says.
/// </summary>
public sealed record RecordDeploymentRequest(
    string? Version,
    string? Ref,
    DateTimeOffset? At,
    string? Ticket,
    string? Note)
{
    /// <inheritdoc cref="CorrectDeploymentRequest.UnknownFields"/>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// What a caller sends to correct one: the four fields a correction may touch,
/// and no others.
/// </summary>
public sealed record CorrectDeploymentRequest(
    string? Ref,
    DateTimeOffset? At,
    string? Ticket,
    string? Note)
{
    /// <summary>
    /// Whatever the caller sent that this object does not define. <c>version</c>
    /// and <c>installation</c> are what the record <em>is</em>, so a deployment
    /// with the wrong version is deleted and recorded again — the refusal says
    /// so rather than leaving a caller guessing.
    /// </summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// Turns deployment rows into the two shapes. Everything derived comes from
/// <see cref="Derived"/> and from nowhere else, which is what keeps a read from
/// answering by the order of recording.
/// </summary>
public sealed class DeploymentAssembler(IIdentities identities, IFiles files)
{
    public async Task<IReadOnlyList<DeploymentSummaryShape>> SummariesAsync(
        IReadOnlyList<Deployment> all, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(all);

        var people = await identities.FindManyAsync(
            all.SelectMany(one => new[] { one.CreatedBy }).Distinct(), cancellationToken);

        return
        [
            .. Derived.InOrder(all)
                .Reverse()
                .Select(one => new DeploymentSummaryShape(
                    one.Number,
                    one.Version,
                    Derived.Previous(all, one),
                    one.Ref,
                    one.At,
                    IdentityRef.Of(people[one.CreatedBy]),
                    one.Ticket,
                    one.UpdatedAt)),
        ];
    }

    public async Task<DeploymentShape> CompleteAsync(
        Installation installation,
        IReadOnlyList<Deployment> all,
        Deployment one,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(one);

        var people = await identities.FindManyAsync([one.CreatedBy, one.UpdatedBy], cancellationToken);

        var owner = new Domain.Files.FileOwner(
            Domain.Files.OwnerKind.Installation, installation.Id, installation.Key);

        return new DeploymentShape(
            installation.Key,
            one.Number,
            one.Version,
            Derived.Previous(all, one),
            one.Ref,
            [.. Derived.FilesAt(await files.ListAsync(owner, cancellationToken), one.At).Select(DeploymentFileShape.Of)],
            one.At,
            IdentityRef.Of(people[one.CreatedBy]),
            one.Ticket,
            one.Note,
            one.CreatedAt,
            IdentityRef.Of(people[one.UpdatedBy]),
            one.UpdatedAt);
    }
}

/// <summary>The lookup every deployment act starts with: the installation, then the number.</summary>
public sealed class DeploymentLookup(
    IInstallations installations, IDeployments deployments, InstanceSettings settings)
{
    public Task<Installation> InstallationAsync(string key, CancellationToken cancellationToken) =>
        installations.LiveAsync(key, settings, cancellationToken);

    public Task<IReadOnlyList<Deployment>> AllAsync(Installation installation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        return deployments.ListAsync([installation.Id], cancellationToken);
    }

    /// <exception cref="Refusal"><c>not-found</c>, or <c>deleted</c> with <c>restorable_until</c>.</exception>
    public async Task<Deployment> LiveAsync(
        Installation installation, int number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var deployment = await deployments.FindAnyAsync(installation.Id, number, cancellationToken)
            ?? throw new Refusal(RefusalCode.NotFound, $"{installation.Key} has no deployment {number}.");

        return deployment.Deleted
            ? throw new Refusal(
                RefusalCode.Deleted,
                $"Deployment {number} of {installation.Key} is deleted and can be restored until at least {deployment.DeletedAt!.Value + settings.DeletionGrace:u}.",
                new Dictionary<string, object?> { ["restorable_until"] = deployment.DeletedAt.Value + settings.DeletionGrace })
            : deployment;
    }
}

/// <summary>
/// Every deployment of one installation, newest by <c>at</c> first — the order
/// everything derived uses, so that the list and the version agree.
/// </summary>
public sealed class ListDeployments(DeploymentLookup lookup, DeploymentAssembler assembler)
{
    public async Task<IReadOnlyList<DeploymentSummaryShape>> ExecuteAsync(
        string key, CancellationToken cancellationToken)
    {
        var installation = await lookup.InstallationAsync(key, cancellationToken);
        return await assembler.SummariesAsync(await lookup.AllAsync(installation, cancellationToken), cancellationToken);
    }
}

public sealed class ReadDeployment(DeploymentLookup lookup, DeploymentAssembler assembler)
{
    public async Task<DeploymentShape> ExecuteAsync(string key, int number, CancellationToken cancellationToken)
    {
        var installation = await lookup.InstallationAsync(key, cancellationToken);

        return await assembler.CompleteAsync(
            installation,
            await lookup.AllAsync(installation, cancellationToken),
            await lookup.LiveAsync(installation, number, cancellationToken),
            cancellationToken);
    }
}

/// <summary>The corrections made to one deployment: who, when, which field, from what to what.</summary>
public sealed class ReadDeploymentHistory(DeploymentLookup lookup, IIdentities identities, IHistory history)
{
    public async Task<IReadOnlyList<HistoryEntryShape>> ExecuteAsync(
        string key, int number, CancellationToken cancellationToken)
    {
        var installation = await lookup.InstallationAsync(key, cancellationToken);
        var deployment = await lookup.LiveAsync(installation, number, cancellationToken);
        var entries = await history.ListAsync(HistorySubject.Deployment, deployment.Id, cancellationToken);

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

/// <summary>A deployment, recorded because it is done.</summary>
public sealed class RecordDeployment(
    ICallerIdentity callerIdentity,
    IDeployments deployments,
    DeploymentLookup lookup,
    IHistory history,
    ITransactions transactions,
    DeploymentAssembler assembler,
    TimeProvider clock)
{
    public async Task<DeploymentShape> ExecuteAsync(
        string key, RecordDeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DeploymentWrites.Closed(request.UnknownFields);
        var caller = callerIdentity.Caller;

        var installation = await lookup.InstallationAsync(key, cancellationToken);

        var recorded = await transactions.RunAsync(async () =>
        {
            var now = clock.GetUtcNow();

            var deployment = DeploymentWrites.Record(
                deployments,
                installation.Id,
                await deployments.NextNumberAsync(installation.Id, cancellationToken),
                request.Version,
                request.At ?? now,
                caller.Id,
                now);

            // What the caller sent with it is part of the birth, not a
            // correction: the history says the row appeared, and the fields it
            // appeared with are the row's own.
            DeploymentWrites.Apply(
                deployment,
                new DeploymentEdit { Ref = request.Ref, Ticket = request.Ticket, Note = request.Note },
                caller.Id,
                now);

            history.Add(HistoryEntry.OnDeployment(
                deployment.Id, caller.Id, now, HistoryField.Created, null, deployment.Version));

            await deployments.SaveAsync(cancellationToken);
            return deployment;
        }, cancellationToken);

        return await assembler.CompleteAsync(
            installation, await lookup.AllAsync(installation, cancellationToken), recorded, cancellationToken);
    }
}

/// <summary>
/// The four fields a correction may touch, guarded by <c>If-Match</c> against
/// <c>updated_at</c>.
/// </summary>
public sealed class CorrectDeployment(
    ICallerIdentity callerIdentity,
    IDeployments deployments,
    DeploymentLookup lookup,
    IHistory history,
    ITransactions transactions,
    DeploymentAssembler assembler,
    TimeProvider clock)
{
    public async Task<DeploymentShape> ExecuteAsync(
        string key, int number, CorrectDeploymentRequest changes, string? ifMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        DeploymentWrites.Closed(changes.UnknownFields);
        var caller = callerIdentity.Caller;

        var installation = await lookup.InstallationAsync(key, cancellationToken);
        var before = await lookup.LiveAsync(installation, number, cancellationToken);
        var expected = GuardedWrite.Expected(ifMatch);

        var corrected = await transactions.RunAsync(async () =>
        {
            var row = await deployments.LoadForWriteAsync(before.Id, cancellationToken)
                ?? throw new Refusal(RefusalCode.NotFound, $"{key} has no deployment {number}.");

            if (expected is { } version && row.UpdatedAt != version)
            {
                throw new Refusal(
                    RefusalCode.Stale,
                    $"Deployment {number} changed at {row.UpdatedAt:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}; you last read it at {version:yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'}.",
                    new Dictionary<string, object?>
                    {
                        ["current"] = await assembler.CompleteAsync(
                            installation, await lookup.AllAsync(installation, cancellationToken), row, cancellationToken),
                    });
            }

            var now = clock.GetUtcNow();

            foreach (var change in DeploymentWrites.Apply(
                row,
                new DeploymentEdit { Ref = changes.Ref, At = changes.At, Ticket = changes.Ticket, Note = changes.Note },
                caller.Id,
                now))
            {
                history.Add(HistoryEntry.OnDeployment(
                    row.Id, caller.Id, now, change.Field, change.OldValue, change.NewValue));
            }

            await deployments.SaveAsync(cancellationToken);
            return row;
        }, cancellationToken);

        return await assembler.CompleteAsync(
            installation, await lookup.AllAsync(installation, cancellationToken), corrected, cancellationToken);
    }
}

internal static class DeploymentWrites
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
                    "version" => "A deployment's version is what the record is; a wrong one is deleted and recorded again.",
                    "installation" => "A deployment belongs to the installation it was recorded under, and does not move.",
                    "number" => "The instance numbers a deployment; it is not given.",
                    "previous" or "files" => $"A deployment's {field} is derived from the deployments and the file revisions, and is never written.",
                    "status" => "A deployment has no status: it is recorded when it is done.",
                    _ => $"A deployment has no field {field}.",
                },
                new Dictionary<string, object?> { ["field"] = field });
        }
    }

    /// <summary>The row itself, with a refusal that names the field when the version does not hold.</summary>
    public static Deployment Record(
        IDeployments deployments,
        Guid installationId,
        int number,
        string? version,
        DateTimeOffset at,
        Guid by,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deployments);

        var deployment = Validated.Field(
            "version", () => Deployment.Record(installationId, number, version, at, by, now));

        deployments.Add(deployment);
        return deployment;
    }

    /// <summary>The deployment applies what it was given, and a bad value is a refusal that names its field.</summary>
    public static IReadOnlyList<FieldChange> Apply(
        Deployment deployment, DeploymentEdit edit, Guid by, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        try
        {
            return deployment.Apply(edit, by, now);
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(refusal.ParamName ?? "deployment", Validated.Said(refusal));
        }
    }
}
