namespace Hostingaffe.Domain.Installations;

/// <summary>
/// Whether anything watches the installation from outside
/// (<c>CONTEXT.md</c>, Installation). Closed.
/// </summary>
public enum Monitoring
{
    None,

    /// <summary>Watched from off the machine, which is the only watching that survives the machine.</summary>
    External,
}
