using Hostingaffe.Domain;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The register of keys that have been given out (<c>docs/storage.md</c>,
/// Assigned keys). It answers whether a key is reserved. The explicit purge
/// can release an installation key (ADR 0019).
/// </summary>
public interface IKeys
{
    /// <summary>Whether this kind of thing has ever had a thing by this key.</summary>
    Task<bool> AssignedAsync(Keyed kind, string key, CancellationToken cancellationToken);

    /// <summary>Writes the key down as spent. Done with the row that takes it, in the same transaction.</summary>
    void Assign(Keyed kind, string key);
}
