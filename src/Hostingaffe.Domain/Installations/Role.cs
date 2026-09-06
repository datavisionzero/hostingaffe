namespace Hostingaffe.Domain.Installations;

/// <summary>
/// What an installation is for the machine (<c>CONTEXT.md</c>, Installation).
/// Closed.
/// </summary>
/// <remarks>
/// A third word <c>infrastructure</c> was weighed in VISION 7 and rejected,
/// because Caddy would then have been neither <c>application</c> nor
/// <c>platform</c>. It does not come back here.
/// </remarks>
public enum Role
{
    /// <summary>What the machine is there for.</summary>
    Application,

    /// <summary>What the host runs for everything else on it — Caddy, a monitor, a notifier.</summary>
    Platform,
}
