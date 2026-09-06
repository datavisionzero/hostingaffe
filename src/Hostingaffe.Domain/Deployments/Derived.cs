using Hostingaffe.Domain.Files;

using File = Hostingaffe.Domain.Files.File;

namespace Hostingaffe.Domain.Deployments;

/// <summary>
/// One file of an installation as a deployment found it: which path, and which
/// revision was current when the version went live.
/// </summary>
public sealed record DeployedFile(string Path, int Revision);

/// <summary>
/// Everything the record computes rather than stores, in one place
/// (<c>docs/storage.md</c>, What is derived rather than stored).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything here is ordered by <c>at</c>, never by the order of
/// recording.</strong> Whoever counts by recording order moves the present
/// every time somebody backfills the past, and that is the mistake VISION 7
/// names by name. <c>at</c> can repeat — two deployments in the same
/// microsecond, or two backfilled to the same day — so the number breaks the
/// tie, and it is the only thing that does.
/// </para>
/// <para>
/// It is one place on purpose. A query that forgets the rule is how decisions
/// like this fail, so there is one rule and everything reads through it.
/// </para>
/// </remarks>
public static class Derived
{
    /// <summary>The deployments oldest first, by <c>at</c> and then by number.</summary>
    public static IReadOnlyList<Deployment> InOrder(IEnumerable<Deployment> deployments)
    {
        ArgumentNullException.ThrowIfNull(deployments);
        return [.. deployments.OrderBy(one => one.At).ThenBy(one => one.Number)];
    }

    /// <summary>
    /// The version an installation runs: the one of its latest deployment by
    /// <c>at</c>, or nothing where none has been recorded.
    /// </summary>
    public static string? Version(IEnumerable<Deployment> deployments) => Latest(deployments)?.Version;

    /// <summary>The latest deployment by <c>at</c>, or nothing at all.</summary>
    public static Deployment? Latest(IEnumerable<Deployment> deployments)
    {
        var ordered = InOrder(deployments);
        return ordered.Count > 0 ? ordered[^1] : null;
    }

    /// <summary>
    /// The version that ran before this deployment: the one of the deployment
    /// before it in the same order, or nothing where it is the first.
    /// </summary>
    public static string? Previous(IEnumerable<Deployment> deployments, Deployment one)
    {
        ArgumentNullException.ThrowIfNull(one);

        var ordered = InOrder(deployments);
        var at = ordered.ToList().FindIndex(other => other.Id == one.Id);

        return at > 0 ? ordered[at - 1].Version : null;
    }

    /// <summary>
    /// The revisions of the installation's files that were current at
    /// <paramref name="at"/>. Empty for a deployment backfilled to before the
    /// first file was put — which is the honest answer, not a missing one.
    /// </summary>
    public static IReadOnlyList<DeployedFile> FilesAt(IEnumerable<File> files, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(files);

        return
        [
            .. files
                .Select(file => (file.Path, Revision: Current(file, at)))
                .Where(found => found.Revision is not null)
                .OrderBy(found => found.Path, StringComparer.Ordinal)
                .Select(found => new DeployedFile(found.Path, found.Revision!.Number)),
        ];
    }

    private static FileRevision? Current(File file, DateTimeOffset at) =>
        file.Revisions
            .Where(revision => revision.At <= at)
            .OrderByDescending(revision => revision.Number)
            .FirstOrDefault();
}
