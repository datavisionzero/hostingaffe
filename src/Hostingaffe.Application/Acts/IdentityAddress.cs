using Hostingaffe.Application.Ports;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// How a path addresses an identity: by its id, or by its name. A name is
/// unique across users and agents whatever the case and never has the shape of
/// a UUID, so the two cannot be taken for one another — and the id stays the
/// address that survives a rename (<c>docs/api.md</c>).
/// </summary>
internal static class IdentityAddress
{
    /// <summary>
    /// The id <paramref name="address"/> stands for, or <c>null</c> when it
    /// names nobody. A caller answers that the way it answers an id that
    /// belongs to nobody: the two are the same miss.
    /// </summary>
    public static async Task<Guid?> ResolveAsync(
        string? address, IIdentities identities, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Guid.TryParse(address, out var id)
            ? id
            : (await identities.FindByNameAsync(address.Trim(), cancellationToken))?.Id;
    }
}
