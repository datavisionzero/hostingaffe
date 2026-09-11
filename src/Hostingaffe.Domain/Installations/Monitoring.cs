namespace Hostingaffe.Domain.Installations;

/// <summary>
/// Whether anything watches the installation from outside
/// (<c>CONTEXT.md</c>, Installation). Closed, and three values rather than two
/// for the same reason as <see cref="Backup"/>: "every production installation
/// nobody watches" has to stop asking about the ones that were decided.
/// </summary>
public enum Monitoring
{
    /// <summary>None, decided or not — which is the same thing to whoever misses the outage.</summary>
    None,

    /// <summary>Decided on, not there yet.</summary>
    Planned,

    /// <summary>Watched from off the machine, which is the only watching that survives the machine.</summary>
    External,
}
