namespace Hostingaffe.Domain.Pages;

/// <summary>
/// What a page is to be read as (<c>CONTEXT.md</c>, Page). Closed.
/// </summary>
/// <remarks>
/// A <c>decision</c> is a page whose kind says it should be read as one, and
/// nothing more: no status, no supersedes, no template enforced beyond the kind
/// (VISION 7). <c>note</c> is the kind that claims nothing, which is why it is
/// what pages written before this field existed became.
/// </remarks>
public enum PageKind
{
    /// <summary>How something is done: the backup runbook of a host, the restore of a database.</summary>
    Runbook,

    /// <summary>Why something is the way it is.</summary>
    Decision,

    /// <summary>Everything else, and the kind that claims nothing.</summary>
    Note,
}
