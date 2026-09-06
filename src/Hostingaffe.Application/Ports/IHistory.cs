using Hostingaffe.Domain.History;

namespace Hostingaffe.Application.Ports;

/// <summary>The history rows: appended, never edited (<c>CONTEXT.md</c>).</summary>
public interface IHistory
{
    void Add(HistoryEntry entry);

    /// <summary>Every entry about one subject, oldest first. Not paginated.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistorySubject subject, Guid subjectId, CancellationToken cancellationToken);
}
