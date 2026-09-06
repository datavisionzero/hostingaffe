using Hostingaffe.Domain.Projects;
using Hostingaffe.Domain.Releases;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The project rows, and the two counters on them every key is drawn from.
/// </summary>
public interface IProjects
{
    /// <summary>By key, deleted or not — whether a deleted one counts is the act's question.</summary>
    Task<Project?> FindByKeyAsync(string key, CancellationToken cancellationToken);

    /// <summary>A fresh project snapshot for a waiting read; never served from the change tracker.</summary>
    Task<Project?> FindByKeyForReadAsync(string key, CancellationToken cancellationToken);

    Task<Project?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether the key is taken, by a live project or by one waiting out its grace period.</summary>
    Task<bool> KeyTakenAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every live project, by key.</summary>
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Project>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>The project and its first labels in one transaction.</summary>
    /// <exception cref="Domain.Refusal"><c>validation</c> on <c>key</c> when the unique index refuses it.</exception>
    Task AddAsync(Project project, IEnumerable<Label> labels, Release release, ProjectAccess creatorAccess,
        CancellationToken cancellationToken);

    Task SaveAsync(Project project, CancellationToken cancellationToken);

    /// <summary>
    /// Draws <paramref name="count"/> issue numbers from the project's counter
    /// in one statement under the row's lock, and returns the first of them
    /// (<c>docs/storage.md</c>, Keys are allocated from the project row). Inside
    /// the caller's transaction: a rollback rolls the counter back, so keys are
    /// dense and never reused.
    /// </summary>
    Task<int> AllocateIssueNumbersAsync(Guid projectId, int count, CancellationToken cancellationToken);

    /// <inheritdoc cref="AllocateIssueNumbersAsync"/>
    Task<int> AllocateEpicNumbersAsync(Guid projectId, int count, CancellationToken cancellationToken);
}
