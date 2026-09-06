using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// A machine or an installation as the contract carries it:
/// <c>{ "kind": "machine", "key": "ex44" }</c>. It is what a file's
/// <c>owner</c> and a page's <c>attached_to</c> both are.
/// </summary>
/// <remarks>
/// The kind is part of the answer, because a key alone would name a machine and
/// an installation at once (<c>CONTEXT.md</c>, Key).
/// </remarks>
public sealed record AnchorShape(AnchorKind Kind, string Key)
{
    public static AnchorShape Of(Anchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return new AnchorShape(anchor.Kind, anchor.Key);
    }
}

/// <summary>
/// The one place that turns a named anchor into a row and a row back into a
/// name — for the file that hangs on one and the page that is attached to one.
/// </summary>
public sealed class Anchorage(IMachines machines, IInstallations installations, InstanceSettings settings)
{
    /// <summary>
    /// The anchor an address named. A machine and an installation are looked up
    /// the way they always are, so a deleted one says <c>deleted</c> and an
    /// unknown one says <c>not-found</c>.
    /// </summary>
    public async Task<Anchor> ResolveAsync(AnchorKind kind, string key, CancellationToken cancellationToken)
    {
        var id = kind is AnchorKind.Machine
            ? (await machines.LiveAsync(key, settings, cancellationToken)).Id
            : (await installations.LiveAsync(key, settings, cancellationToken)).Id;

        return new Anchor(kind, id, key?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// The anchor a <em>field</em> named, or nothing where the field named
    /// none. What does not exist is <c>validation</c> on the field it arrived
    /// in, not a <c>not-found</c> about an address nobody asked for.
    /// </summary>
    public async Task<Anchor?> FromAsync(
        AnchorShape? given, string field, CancellationToken cancellationToken)
    {
        if (given is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(given.Key))
        {
            throw Refusal.Validation(field, "An anchor names a machine or an installation by its key.");
        }

        if (!Enum.IsDefined(given.Kind))
        {
            throw Refusal.Validation(field, "An anchor is a machine or an installation.");
        }

        return await Validated.FieldAsync(
            field, () => ResolveAsync(given.Kind, given.Key, cancellationToken));
    }

    /// <summary>
    /// The keys of the given machine and installation rows, by row id, so that
    /// a whole list of anchors is resolved in two reads rather than in two per
    /// row.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, AnchorShape>> NamesAsync(
        IEnumerable<Guid?> machineIds, IEnumerable<Guid?> installationIds, CancellationToken cancellationToken)
    {
        var named = new Dictionary<Guid, AnchorShape>();

        foreach (var (id, key) in await machines.KeysAsync(Present(machineIds), cancellationToken))
        {
            named[id] = new AnchorShape(AnchorKind.Machine, key);
        }

        foreach (var (id, key) in await installations.KeysAsync(Present(installationIds), cancellationToken))
        {
            named[id] = new AnchorShape(AnchorKind.Installation, key);
        }

        return named;
    }

    private static IEnumerable<Guid> Present(IEnumerable<Guid?> ids) =>
        (ids ?? []).Where(id => id is not null).Select(id => id!.Value).Distinct();
}
