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
/// rows of each kind whose grace period has passed are removed, plus up to
/// twenty idempotency rows older than a day. The batch is small so that no
/// request pays for a backlog, and the floor is a floor: an instance nobody
/// writes to keeps its deleted rows longer. No scheduler; the write that would
/// have paid for one does the work instead.
/// </para>
/// <para>
/// It runs from the leaves inward — deployments and files, then installations,
/// then machines and software — and every step that removes a parent asks that
/// nothing still points at it, because the batch is capped and a child may be
/// waiting for the next write. What is left standing is picked up next time.
/// </para>
/// <para>
/// <strong>Reports are swept on the same stroke</strong>, past the retention
/// window and never the latest of a machine. A report is itself a write, so
/// this runs once per arriving report and clears twenty for every one that
/// comes in — which is what keeps a cron reporting every quarter of an hour
/// from outrunning it.
/// </para>
/// <para>
/// <strong>The history is never purged</strong>, and the automatic sweep leaves
/// the register of keys alone. A separate installation purge can release its
/// key (ADR 0019). A page is not purged with its anchor either — it loses the
/// anchor and becomes a page of the instance.
/// </para>
/// </remarks>
public sealed class Transactions(HostingaffeDbContext context, InstanceSettings settings) : ITransactions
{
    /// <summary>Rows per kind per transaction.</summary>
    public const int Batch = 20;

    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // An act inside an act joins the transaction it is already in rather
        // than opening a second one: the bulk write is one act made of the
        // ordinary ones, and "all or nothing" is what makes it one. The
        // outermost call commits, purges, and rolls the lot back — an inner
        // one that cleared the change tracker would throw away what the outer
        // is still holding.
        if (context.Database.CurrentTransaction is not null)
        {
            return await work();
        }

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

        // The leaves first. A deployment and a file hold nothing up either.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from deployment where id in (
                select id from deployment
                 where deleted_at is not null and deleted_at <= now() - {0}::interval
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from file where id in (
                select id from file
                 where deleted_at is not null and deleted_at <= now() - {0}::interval
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        // A page does not follow its anchor into deletion, but it cannot name
        // one that is gone for good either: at the purge it loses the anchor and
        // becomes a page of the instance.
        await context.Database.ExecuteSqlRawAsync(
            """
            update page set installation_id = null
             where installation_id in (
                select id from installation
                 where deleted_at is not null and deleted_at <= now() - {0}::interval)
            """,
            [settings.DeletionGrace], cancellationToken);

        // The same for a dependency, and for the same reason a page loses its
        // anchor: an installation others depend on is never deleted out from
        // under them, so an edge can only reach the purge from a dependent that
        // was itself deleted — and it cannot name something that is gone for
        // good. The dependent keeps its other edges and loses this one.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from installation_depends_on
             where depends_on_id in (
                select id from installation
                 where deleted_at is not null and deleted_at <= now() - {0}::interval)
            """,
            [settings.DeletionGrace], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from installation where id in (
                select i.id from installation i
                 where i.deleted_at is not null and i.deleted_at <= now() - {0}::interval
                   and not exists (select 1 from file f where f.installation_id = i.id)
                   and not exists (select 1 from deployment d where d.installation_id = i.id)
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            update page set machine_id = null
             where machine_id in (
                select id from machine
                 where deleted_at is not null and deleted_at <= now() - {0}::interval)
            """,
            [settings.DeletionGrace], cancellationToken);

        // A report and a machine's token are reached only through the machine,
        // so neither carries a deletion of its own: they go when it goes. Not
        // batched, because the batch is a machine — a month of samples is a few
        // thousand narrow rows, and leaving half of them behind would only hold
        // the machine back for another write.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from machine_report
             where machine_id in (
                select id from machine
                 where deleted_at is not null and deleted_at <= now() - {0}::interval)
            """,
            [settings.DeletionGrace], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from machine_token
             where machine_id in (
                select id from machine
                 where deleted_at is not null and deleted_at <= now() - {0}::interval)
            """,
            [settings.DeletionGrace], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from machine where id in (
                select m.id from machine m
                 where m.deleted_at is not null and m.deleted_at <= now() - {0}::interval
                   and not exists (select 1 from file f where f.machine_id = m.id)
                   and not exists (select 1 from installation i where i.machine_id = m.id)
                   and not exists (select 1 from machine g where g.host_id = m.id)
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from software where id in (
                select s.id from software s
                 where s.deleted_at is not null and s.deleted_at <= now() - {0}::interval
                   and not exists (select 1 from installation i where i.software_id = s.id)
                 limit {1})
            """,
            [settings.DeletionGrace, Batch], cancellationToken);

        // The reports past the retention window, which is thirty days unless an
        // operator said otherwise. **The latest report of every machine is
        // exempt however old it is**: a machine that fell silent six weeks ago
        // must keep the one thing worth knowing about it — when it last spoke,
        // and how it was doing then (ADR 0015).
        //
        // The batch keeps up by itself: a report is a write, so this runs once
        // per arriving report and clears twenty for every one that comes in.
        if (settings.SweepsReports)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                delete from machine_report where id in (
                    select r.id from machine_report r
                     where r.received_at < now() - {0}::interval
                       and r.received_at < (
                            select max(latest.received_at) from machine_report latest
                             where latest.machine_id = r.machine_id)
                     limit {1})
                """,
                [settings.ReportRetention, Batch], cancellationToken);
        }

        // A device login is worth nothing ten minutes after it was made, whether
        // it was approved, refused or never answered. A day's grace, so that a
        // person who ran `ha login` and walked away still reads why it failed.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from device_login where id in (
                select id from device_login
                 where expires_at <= now() - interval '24 hours'
                 limit {0})
            """,
            [Batch], cancellationToken);

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
