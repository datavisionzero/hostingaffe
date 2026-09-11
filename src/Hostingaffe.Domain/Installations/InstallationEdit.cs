using Hostingaffe.Domain.Machines;

namespace Hostingaffe.Domain.Installations;

/// <summary>
/// What a caller wants an installation to say. Every field is optional;
/// <c>null</c> leaves the field alone, the empty string clears a text field,
/// and an empty list clears a list (<c>docs/api.md</c>, Installations).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Machine"/> and <see cref="Software"/> are rows rather than keys:
/// the Domain does not resolve keys, so the act does that and hands the rows
/// down. Neither can be cleared — an installation is a software on a machine,
/// and one without either is not an installation.
/// </para>
/// <para>
/// <see cref="DependsOn"/> is rows for the reason those two are, and is the one
/// list here that is not made of value objects: what an installation depends on
/// is other installations, and the Domain is handed them rather than their keys.
/// </para>
/// <para>
/// There is no <c>version</c> here and no <c>needed_by</c>. Both are derived —
/// the first from the deployments, the second from the dependencies read the
/// other way — and neither is a field an edit can carry.
/// </para>
/// </remarks>
public sealed record InstallationEdit
{
    public string? Name { get; init; }

    public Machine? Machine { get; init; }

    public Software? Software { get; init; }

    public Environment? Environment { get; init; }

    public Role? Role { get; init; }

    public Status? Status { get; init; }

    public IReadOnlyList<string>? Urls { get; init; }

    public IReadOnlyList<Port>? Ports { get; init; }

    public string? Path { get; init; }

    public string? Data { get; init; }

    public IReadOnlyList<Secret>? Secrets { get; init; }

    /// <summary>The installations this one needs; an empty list clears them.</summary>
    public IReadOnlyList<Installation>? DependsOn { get; init; }

    public Backup? Backup { get; init; }

    public Monitoring? Monitoring { get; init; }

    public Logging? Logging { get; init; }

    public string? Description { get; init; }
}
