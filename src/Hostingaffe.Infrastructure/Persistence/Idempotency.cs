using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>The idempotency rows; the purge takes those older than a day.</summary>
public sealed class Idempotency(HostingaffeDbContext context) : IIdempotency
{
    public async Task<StoredReply?> FindAsync(Guid identityId, string key, CancellationToken cancellationToken)
    {
        var row = await context.Idempotency
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdentityId == identityId && r.Key == key, cancellationToken);

        return row is null ? null : new StoredReply(row.RequestHash, row.Status, row.Body, row.CreatedAt);
    }

    public async Task StoreAsync(Guid identityId, string key, StoredReply reply, CancellationToken cancellationToken)
    {
        // A stale row — older than a day, not yet purged — is replaced; a
        // concurrent twin of this very request loses on the primary key, and
        // the reply it got was as good as ours.
        await context.Idempotency
            .Where(r => r.IdentityId == identityId && r.Key == key && r.CreatedAt <= reply.CreatedAt.AddHours(-24))
            .ExecuteDeleteAsync(cancellationToken);

        context.Idempotency.Add(IdempotencyRecord.Of(identityId, key, reply.RequestHash, reply.Status, reply.Body, reply.CreatedAt));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException collision) when (collision.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
        }
    }
}
