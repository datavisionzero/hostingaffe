using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The schema against a real Postgres. EF Core owning the migrations is what
/// lets an installation be <c>docker compose up</c> and nothing else, so the
/// thing worth proving is that they apply — that applying them twice is
/// uneventful, because two containers starting at once is an ordinary event —
/// and that what they create is what <c>docs/storage.md</c> says.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = Migrated.ContextFor(connectionString))
        {
            Assert.NotEmpty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            await Migrated.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = Migrated.ContextFor(connectionString))
        {
            Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Applying_twice_finds_nothing_to_do()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using var first = Migrated.ContextFor(connectionString);
        await Migrated.MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);

        await using var second = Migrated.ContextFor(connectionString);
        await Migrated.MigratorFor(second).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await second.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Migrations only run forward (ADR 0011), so an old image in front of a
    /// database a later version migrated has to refuse rather than serve.
    /// Asking for pending migrations cannot say that — there are none.
    /// </summary>
    [Fact]
    public async Task A_schema_from_a_newer_hostingaffe_is_refused()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        await using (var context = Migrated.ContextFor(connectionString))
        {
            await Migrated.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('29991231235959_SomethingThisVersionNeverHeardOf', '10.0.0')
                """,
                TestContext.Current.CancellationToken);
        }

        await using (var context = Migrated.ContextFor(connectionString))
        {
            var refusal = await Assert.ThrowsAsync<SchemaIsNewerException>(
                () => Migrated.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken));

            Assert.Equal(["29991231235959_SomethingThisVersionNeverHeardOf"], refusal.Migrations);
        }
    }

    [Fact]
    public async Task The_tables_are_the_ones_storage_md_declares()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);

        var tables = await NamesAsync(
            migrated.ConnectionString,
            "select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE'");

        Assert.Equal(
            [
                "__EFMigrationsHistory",
                "blocker", "browser_session", "comment", "epic", "epic_label", "history", "idempotency", "identity", "identity_metadata",
                "issue", "issue_label", "label", "one_time_secret", "page", "page_label", "project", "project_access", "question", "release", "release_issue", "token",
            ],
            tables);
    }

    /// <summary>
    /// Every index is declared, none is inferred: the list is the one in
    /// <c>docs/storage.md</c>, plus a primary key per table. The two the model
    /// cannot express — the expression index on the name and the view — are SQL
    /// in the migration, and this is what proves they arrived.
    /// </summary>
    [Fact]
    public async Task The_indexes_are_the_ones_storage_md_declares()
    {
        await using var migrated = await Migrated.EmptyAsync(postgres);

        var indexes = await NamesAsync(
            migrated.ConnectionString,
            "select indexname from pg_indexes where schemaname = 'public'");

        Assert.Equal(
            [
                "PK___EFMigrationsHistory",
                "blocker_blocked", "browser_session_hash", "browser_session_user", "comment_issue", "comment_search", "epic_number", "history_epic", "history_issue", "history_page",
                "identity_email", "identity_metadata_identity", "identity_name", "issue_assignee", "issue_claim", "issue_epic", "issue_next",
                "issue_number", "issue_parent", "issue_search", "issue_updated", "label_name",
                "one_live_secret_per_purpose", "one_time_secret_hash", "page_search", "page_slug", "pk_blocker", "pk_browser_session", "pk_comment", "pk_epic", "pk_epic_label", "pk_history", "pk_idempotency",
                "pk_identity", "pk_identity_metadata", "pk_issue", "pk_issue_label", "pk_label", "pk_one_time_secret", "pk_page", "pk_page_label", "pk_project", "pk_project_access", "pk_question", "pk_release",
                "pk_release_issue", "pk_token", "project_access_user", "project_key", "question_issue", "question_open", "question_search", "release_issue_issue",
                "release_name", "release_open", "token_agent", "token_secret_hash",
            ],
            indexes);

        Assert.Equal(
            ["issue_read"],
            await NamesAsync(migrated.ConnectionString, "select viewname from pg_views where schemaname = 'public'"));
    }

    private static async Task<IReadOnlyList<string>> NamesAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var names = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return [.. names.Order(StringComparer.Ordinal)];
    }
}
