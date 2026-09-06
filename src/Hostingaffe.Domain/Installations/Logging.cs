namespace Hostingaffe.Domain.Installations;

/// <summary>
/// Where the installation's logs end up (<c>CONTEXT.md</c>, Installation).
/// Closed.
/// </summary>
public enum Logging
{
    /// <summary>On the machine, and gone with it.</summary>
    Local,

    /// <summary>Shipped somewhere that outlives the machine.</summary>
    Central,
}
