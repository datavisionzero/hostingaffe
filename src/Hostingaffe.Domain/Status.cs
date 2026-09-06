namespace Hostingaffe.Domain;

/// <summary>
/// Where a machine or an installation stands (<c>CONTEXT.md</c>, Machine and
/// Installation; VISION 7). One set for both, because the vision gives both the
/// same three words and a second copy would be the one that drifts.
/// </summary>
public enum Status
{
    /// <summary>Decided on, not there yet.</summary>
    Planned,

    /// <summary>There, and in use.</summary>
    Active,

    /// <summary>
    /// The normal end. A retired thing keeps everything it had, so that "what
    /// did we run in 2026" stays answerable; it leaves the default lists and
    /// stays reachable by its key.
    /// </summary>
    Retired,
}
