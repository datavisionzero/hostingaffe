using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Application.Ports;

public interface IReleases
{
    Task<IReadOnlyList<Release>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Release?> FindAsync(Guid projectId, string name, CancellationToken cancellationToken);
    Task<Release?> LoadForWriteAsync(Guid projectId, string name, CancellationToken cancellationToken);
    Task<Release?> LoadOpenForWriteAsync(Guid projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<IssueRow>> IssuesAsync(Guid releaseId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> CurrentNamesAsync(IReadOnlyCollection<Guid> issueIds, CancellationToken cancellationToken);
    Task<bool> NameTakenAsync(Guid projectId, string name, CancellationToken cancellationToken);

    /// <summary>The most recently published release of the project, or none.</summary>
    Task<Release?> LatestPublishedAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>How many issues the release records. The open one is empty right after a publication.</summary>
    Task<int> IssueCountAsync(Guid releaseId, CancellationToken cancellationToken);

    /// <summary>Put the issue into the release; <c>false</c> where it was already in it.</summary>
    Task<bool> AttachAsync(Guid releaseId, Guid issueId, CancellationToken cancellationToken);

    /// <summary>Take the issue out of the release; <c>false</c> where it was not in it.</summary>
    Task<bool> DetachAsync(Guid releaseId, Guid issueId, CancellationToken cancellationToken);

    /// <summary>Drop a release row — the empty open one a retracted publication replaces.</summary>
    void Remove(Release release);
    Task<bool> InPublishedAsync(Guid issueId, CancellationToken cancellationToken);
    Task AddDoneAsync(Issue issue, CancellationToken cancellationToken);
    Task RemoveFromOpenAsync(Guid issueId, CancellationToken cancellationToken);
    void Add(Release release);
    Task SaveAsync(CancellationToken cancellationToken);
}
