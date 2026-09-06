using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Domain.Releases;

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
/// <strong>The purge is opportunistic.</strong> Before the commit, for every
/// project a written row belongs to, up to twenty of that project's deleted
/// issues, epics, pages and labels whose grace period has passed are removed — the
/// cascades taking comments, questions, history and edges with them — plus up
/// to twenty idempotency rows older than a day, and up to twenty deleted
/// projects past their grace period, instance-wide. The batch is small so that
/// no request pays for a backlog; the floor is a floor, and a project nobody
/// writes to keeps its deleted rows longer. No scheduler, for the reason VISION
/// 11 gives for the expired claim.
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
        var projects = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted or EntityState.Unchanged)
            .Select(e => e.Entity switch
            {
                Issue issue => issue.ProjectId,
                Epic epic => epic.ProjectId,
                Label label => label.ProjectId,
                Page page => page.ProjectId,
                Project project => project.Id,
                Release release => release.ProjectId,
                _ => (Guid?)null,
            })
            .OfType<Guid>()
            .Distinct()
            .ToArray();

        var grace = settings.DeletionGrace;

        foreach (var projectId in projects)
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                delete from issue where id in (
                    select id from issue
                     where project_id = {0} and deleted_at is not null and deleted_at <= now() - {1}::interval
                     limit {2})
                """,
                [projectId, grace, Batch], cancellationToken);

            // An epic still referenced by an issue — a deleted one whose own
            // grace period has not passed, say — waits for it.
            await context.Database.ExecuteSqlRawAsync(
                """
                delete from epic where id in (
                    select e.id from epic e
                     where e.project_id = {0} and e.deleted_at is not null and e.deleted_at <= now() - {1}::interval
                       and not exists (select 1 from issue i where i.epic_id = e.id)
                     limit {2})
                """,
                [projectId, grace, Batch], cancellationToken);

            // A page holds nothing else up: its slug comes free with the row.
            await context.Database.ExecuteSqlRawAsync(
                """
                delete from page where id in (
                    select id from page
                     where project_id = {0} and deleted_at is not null and deleted_at <= now() - {1}::interval
                     limit {2})
                """,
                [projectId, grace, Batch], cancellationToken);

            await context.Database.ExecuteSqlRawAsync(
                """
                delete from label where id in (
                    select id from label
                     where project_id = {0} and deleted_at is not null and deleted_at <= now() - {1}::interval
                     limit {2})
                """,
                [projectId, grace, Batch], cancellationToken);
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            delete from idempotency where (identity_id, key) in (
                select identity_id, key from idempotency
                 where created_at <= now() - interval '24 hours'
                 limit {0})
            """,
            [Batch], cancellationToken);

        // A deleted project goes with everything in it, on the next write
        // anywhere: the administrator who typed the key decided that.
        await context.Database.ExecuteSqlRawAsync(
            """
            delete from project where id in (
                select id from project
                 where deleted_at is not null and deleted_at <= now() - {0}::interval
                 limit {1})
            """,
            [grace, Batch], cancellationToken);
    }
}
