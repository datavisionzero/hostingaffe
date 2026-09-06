namespace Hostingaffe.Domain.Machines;

/// <summary>
/// What kind of computer it is (<c>CONTEXT.md</c>, Machine). Closed: a value
/// outside this set is refused at the door rather than stored as a row nobody
/// can read.
/// </summary>
/// <remarks>
/// Whether a homelab wants <c>desktop</c> and <c>sbc</c> beside <c>local</c> is
/// an open point of the vision (17.) and is decided there, not by widening this
/// quietly.
/// </remarks>
public enum MachineKind
{
    /// <summary>A rented virtual server.</summary>
    Vps,

    /// <summary>A rented physical server.</summary>
    Dedicated,

    /// <summary>A virtual machine on one of the others; <c>host</c> says on which.</summary>
    Vm,

    /// <summary>A box the operator owns and stands next to.</summary>
    Local,
}
