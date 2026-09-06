using Hostingaffe.Domain;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The software rows (<c>docs/storage.md</c>, Software). A software is found by
/// its key, because that is what an operator names it by
/// (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// The name is singular because the word is: an instance holds
/// <em>software</em>, not <em>softwares</em>, and the plural is circumscribed
/// wherever it is needed. There is no cursor here, for the reason ADR 0012
/// keeps a list slim — one instance holds one team's infrastructure, and the
/// list of what it runs is read in one screen.
/// </remarks>
public interface ISoftware
{
    /// <summary>By key, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<Software?> FindAnyAsync(string key, CancellationToken cancellationToken);

    /// <summary>Every live software, by key.</summary>
    Task<IReadOnlyList<Software>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The keys of the given rows, for the shapes that name one.</summary>
    Task<IReadOnlyDictionary<Guid, string>> KeysAsync(
        IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The row, tracked and locked for the rest of the transaction.</summary>
    Task<Software?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    void Add(Software software);

    Task SaveAsync(CancellationToken cancellationToken);
}
