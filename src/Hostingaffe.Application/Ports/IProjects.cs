using Hostingaffe.Domain.Projects;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The project rows.
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

    /// <summary>The project and the creator's access to it in one transaction.</summary>
    /// <exception cref="Domain.Refusal"><c>validation</c> on <c>key</c> when the unique index refuses it.</exception>
    Task AddAsync(Project project, ProjectAccess creatorAccess, CancellationToken cancellationToken);

    Task SaveAsync(Project project, CancellationToken cancellationToken);
}
