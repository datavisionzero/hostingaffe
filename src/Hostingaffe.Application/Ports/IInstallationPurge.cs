namespace Hostingaffe.Application.Ports;

/// <summary>The irreversible removal of a deleted installation and the release of its key.</summary>
public interface IInstallationPurge
{
    /// <summary>Lock the row and read its current state, bypassing any tracked copy.</summary>
    Task<PurgeTarget?> LockAsync(string key, CancellationToken cancellationToken);

    /// <summary>Other records that still point at this key or row, including deleted records that may be restored.</summary>
    Task<IReadOnlyList<string>> ReferencesAsync(Guid? id, string key, CancellationToken cancellationToken);

    /// <summary>Remove the row, its possessions, and its reservation in the current transaction.</summary>
    Task RemoveAsync(Guid? id, string key, CancellationToken cancellationToken);
}

public sealed record PurgeTarget(Guid Id, Guid MachineId, bool Deleted);
