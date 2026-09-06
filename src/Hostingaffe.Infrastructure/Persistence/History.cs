using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The history rows, appended with the write they record and saved with it.</summary>
public sealed class History(HostingaffeDbContext context) : IHistory
{
    public void Add(HistoryEntry entry) => context.History.Add(entry);

    public async Task<IReadOnlyList<HistoryEntry>> ListAsync(
        HistorySubject subject, Guid subjectId, CancellationToken cancellationToken) =>
        await context.History
            .Where(h => h.Subject == subject && h.SubjectId == subjectId)
            .OrderBy(h => h.Id)
            .ToListAsync(cancellationToken);
}
