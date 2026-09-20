using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Application.Acts;

/// <summary>Release a deleted installation's key after checking every record that can still name it.</summary>
public sealed class PurgeInstallation(
    ICallerIdentity callerIdentity,
    IInstallationPurge purge,
    IKeys keys,
    IHistory history,
    ITransactions transactions,
    TimeProvider clock)
{
    public async Task ExecuteAsync(string key, string? confirm, string? note, CancellationToken cancellationToken)
    {
        key = Validated.Field("key", () => Key.Normalize(key));
        if (confirm != key)
        {
            throw Refusal.Validation("confirm", "Repeat the installation key to confirm its permanent removal.");
        }

        var said = Validated.Note(note);
        var caller = callerIdentity.Caller;

        await transactions.RunAsync(async () =>
        {
            var row = await purge.LockAsync(key, cancellationToken);
            if (row is not null && !row.Deleted)
            {
                throw new Refusal(RefusalCode.Transition, $"Installation {key} must be deleted before it can be purged.");
            }

            if (row is null && !await keys.AssignedAsync(Keyed.Installation, key, cancellationToken))
            {
                throw new Refusal(RefusalCode.NotFound, $"No installation {key} has a reserved key.");
            }

            var references = await purge.ReferencesAsync(row?.Id, key, cancellationToken);
            if (references.Count > 0)
            {
                throw new Refusal(
                    RefusalCode.Transition,
                    $"Installation {key} is still referenced. Clear these references before purging it.",
                    new Dictionary<string, object?> { ["references"] = references });
            }

            var now = clock.GetUtcNow();
            await purge.RemoveAsync(row?.Id, key, cancellationToken);

            // Old history is kept under the old row id. A key whose row was
            // already swept still gets a visible release event, with no machine
            // association because the sweep removed that relationship.
            history.Add(HistoryEntry.OnInstallation(
                row?.Id ?? Guid.NewGuid(), caller.Id, now, HistoryField.Purged, newValue: key, note: said));
            if (row is not null)
            {
                history.Add(HistoryEntry.OnMachine(
                    row.MachineId, caller.Id, now, HistoryField.InstallationPurged, newValue: key, note: said));
            }

            // No entity row is tracked: the store removed it with SQL. Saving
            // the appended history is the only EF write left in this act.
            await history.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}
