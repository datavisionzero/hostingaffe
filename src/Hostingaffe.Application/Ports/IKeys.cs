using Hostingaffe.Domain;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The register of keys that have been given out (<c>docs/storage.md</c>,
/// Assigned keys). It answers one question and holds one rule: a key is never
/// reused, not even after the purge.
/// </summary>
public interface IKeys
{
    /// <summary>Whether this kind of thing has ever had a thing by this key.</summary>
    Task<bool> AssignedAsync(Keyed kind, string key, CancellationToken cancellationToken);

    /// <summary>Writes the key down as spent. Done with the row that takes it, in the same transaction.</summary>
    void Assign(Keyed kind, string key);
}
