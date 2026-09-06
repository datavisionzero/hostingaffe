using Hostingaffe.Domain.Deployments;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The deployment rows (<c>docs/storage.md</c>, Deployments). A deployment has
/// no key: the instance numbers it per installation, and the number is what
/// addresses it.
/// </summary>
/// <remarks>
/// There is one read here and it hands back rows rather than an answer. Every
/// derived value — the current version, the previous one, the file revisions —
/// is computed by <c>Derived</c>, in one place, and a store method that
/// answered "the latest one" would be that rule written a second time, in SQL,
/// where nothing holds the two together. An instance holds one team's
/// infrastructure (VISION 9), and that is what makes the rows affordable.
/// </remarks>
public interface IDeployments
{
    /// <summary>Every live deployment of the given installations, in no order this promises.</summary>
    Task<IReadOnlyList<Deployment>> ListAsync(
        IEnumerable<Guid> installationIds, CancellationToken cancellationToken);

    /// <summary>One by its number under its installation, deleted or not.</summary>
    Task<Deployment?> FindAnyAsync(Guid installationId, int number, CancellationToken cancellationToken);

    /// <summary>The number the next deployment of this installation gets, counted from one.</summary>
    Task<int> NextNumberAsync(Guid installationId, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Deployment?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    void Add(Deployment deployment);

    Task SaveAsync(CancellationToken cancellationToken);
}
