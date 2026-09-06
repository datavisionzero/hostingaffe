using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations on startup, which is what lets an installation be
/// <c>docker compose up</c> and nothing else, and an upgrade a pull and an up
/// (VISION 12, ADR 0011).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Migrations take a lock</strong>, so that two containers starting at
/// once do not migrate against each other — the second waits and then finds
/// nothing to do. A session-level advisory lock is the cheapest way to say that
/// in Postgres, because it needs no table that a migration might itself be
/// creating.
/// </para>
/// <para>
/// <strong>A newer schema than the code is refused</strong>, inside the same
/// lock, so that the comparison is not made against a database another
/// container is in the middle of migrating. A failed migration stops the
/// instance: that is <c>SchemaMigrationService</c> in the Api, which lets both
/// failures out.
/// </para>
/// </remarks>
public sealed class SchemaMigrator(HostingaffeDbContext context, ILogger<SchemaMigrator> logger)
{
    /// <summary>
    /// Any constant serves, as long as every hostingaffe uses the same one.
    /// </summary>
    private const long AdvisoryLockKey = 0x_9A7_AFFE;

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_lock({0})", [AdvisoryLockKey], cancellationToken);

            // Asked first, and asked the other way round from "what is pending":
            // applied migrations the code does not know about. Asking only for
            // pending ones finds nothing on a database a later version has
            // migrated, and an old image would go on to serve requests against
            // a shape it misunderstands.
            var newer = SchemaVersions.NotKnownHere(
                await context.Database.GetAppliedMigrationsAsync(cancellationToken),
                context.Database.GetMigrations());

            if (newer.Count > 0)
            {
                throw new SchemaIsNewerException(newer);
            }

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            if (pending.Length == 0)
            {
                logger.LogInformation("Schema is current; nothing to migrate.");
                return;
            }

            logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Length, pending);

            await context.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("Schema is current.");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_unlock({0})", [AdvisoryLockKey], CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }
}
