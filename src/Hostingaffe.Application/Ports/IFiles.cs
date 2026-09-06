using Hostingaffe.Domain;
using Hostingaffe.Domain.Files;

using File = Hostingaffe.Domain.Files.File;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// The file rows and their revisions (<c>docs/storage.md</c>, Files). A file is
/// found by its owner and its path, because together those are its address;
/// nothing here takes a path alone.
/// </summary>
public interface IFiles
{
    /// <summary>By owner and path, deleted or not — for the <c>deleted</c> answer and for restore.</summary>
    Task<File?> FindAnyAsync(Anchor owner, string path, CancellationToken cancellationToken);

    /// <summary>Every live file of one owner, by path.</summary>
    Task<IReadOnlyList<File>> ListAsync(Anchor owner, CancellationToken cancellationToken);

    /// <summary>
    /// The files of several anchors of one kind: the live ones where
    /// <paramref name="deletedAt"/> is nothing, and otherwise the ones their
    /// owner took with it at exactly that moment.
    /// </summary>
    Task<IReadOnlyList<File>> UnderAsync(
        AnchorKind kind, IEnumerable<Guid> ids, DateTimeOffset? deletedAt, CancellationToken cancellationToken);

    /// <summary>The row and its revisions, tracked and locked for the rest of the transaction.</summary>
    Task<File?> LoadForWriteAsync(Guid id, CancellationToken cancellationToken);

    void Add(File file);

    Task SaveAsync(CancellationToken cancellationToken);
}
