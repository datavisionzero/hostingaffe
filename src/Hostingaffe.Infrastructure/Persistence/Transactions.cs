using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// One database transaction around several store calls on the scoped context,
/// and the purge at its end (ADR 0013, <c>docs/storage.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// An act's refusal rolls it back; nothing inside is visible outside until the
/// commit.
/// </para>
/// <para>
/// <strong>The purge is opportunistic.</strong> Before the commit, up to twenty
/// deleted pages whose grace period has passed are removed, plus up to twenty
/// idempotency rows older than a day. The batch is small so that no request pays
/// for a backlog, and the floor is a floor: an instance nobody writes to keeps
/// its deleted rows longer. No scheduler; the write that would have paid for one
/// does the work instead.
/// </para>
/// </remarks>
public sealed class Transactions(HostingaffeDbContext context, InstanceSettings settings) : ITransactions
{
    /// <summary>Rows per kind per transaction.</summary>
    public const int Batch = 20;

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work();
            await PurgeAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            // The scoped context is also used by the idempotency middleware
            // after a refusal. Do not let its SaveChanges persist entities
            // whose transaction was rolled back.
            context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        // A page holds nothing else up: its slug comes free with the row. Its
        // history stays where it is — VISION 7 wants the history of a thing
        // that is gone to still say that it existed and when it went.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from page where id in (
                select id from page
                 where deleted_at is not null and deleted_at <= now() - {0}::interval
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from idempotency where (identity_id, key) in (
                select identity_id, key from idempotency
                 where created_at <= now() - interval '24 hours'
                 limit {0})
            """,
            [Batch], cancellationToken);
    }
}
