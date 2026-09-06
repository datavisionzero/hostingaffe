using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The two rules that are derived on read rather than written, in the one view
/// every read of an issue goes through (<c>docs/storage.md</c>, What is derived
/// on read): a deleted issue is absent, and an expired claim is no claim, with
/// the status falling back. Nothing writes the fallback — the row keeps saying
/// <c>in_progress</c>, and this is what proves the view does not.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class IssueReadTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_expired_claim_reads_as_todo_and_nobody()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await ClaimedAsync(db, db.Agent.Id, expiresIn: "-1 hour");

        var (status, claimedBy) = await ReadAsync(db.ConnectionString, db.Issue.Id);

        Assert.Equal("todo", status);
        Assert.Null(claimedBy);

        // The row is untouched: the successor writes the trace, not the reader.
        await using var reader = db.Reader();
        var row = await reader.Issues.SingleAsync(i => i.Id == db.Issue.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Domain.Issues.IssueStatus.InProgress, row.Status);
        Assert.NotNull(row.Claim);
        Assert.Equal(db.Agent.Id, row.Claim.HolderId);
        Assert.True(row.Claim.ExpiredAt(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_live_claim_reads_through()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await ClaimedAsync(db, db.Agent.Id, expiresIn: "3 hours");

        var (status, claimedBy) = await ReadAsync(db.ConnectionString, db.Issue.Id);

        Assert.Equal("in_progress", status);
        Assert.Equal(db.Agent.Id, claimedBy);
    }

    [Fact]
    public async Task A_users_claim_never_expires()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        // Taken a year ago and never extended; a user has not crashed, they
        // have gone home (VISION 11).
        await ClaimedAsync(db, db.User.Id, expiresIn: null, since: "-1 year");

        var (status, claimedBy) = await ReadAsync(db.ConnectionString, db.Issue.Id);

        Assert.Equal("in_progress", status);
        Assert.Equal(db.User.Id, claimedBy);
    }

    [Fact]
    public async Task A_deleted_issue_is_absent()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await db.Context.Database.ExecuteSqlRawAsync(
            "update issue set deleted_at = now(), deleted_by = {0} where id = {1}",
            [db.User.Id, db.Issue.Id],
            TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("select count(*) from issue_read", connection);

        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unclaimed_issue_has_no_claim_when_read_back()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // The four columns are one optional dependent on the row: all null is
        // no claim, not a claim held by nobody.
        await using var reader = db.Reader();
        var row = await reader.Issues.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(row.Claim);
    }

    private static Task ClaimedAsync(Migrated db, Guid holder, string? expiresIn, string since = "-5 minutes") =>
        db.Context.Database.ExecuteSqlRawAsync(
            """
            update issue
               set status = 'in_progress',
                   claimed_by = {0},
                   claimed_at = now() + {1}::interval,
                   claim_extended_at = now() + {1}::interval,
                   claim_expires_at = case when {2}::text is null then null else now() + {2}::interval end
             where id = {3}
            """,
            [
                holder,
                since,
                new NpgsqlParameter { Value = expiresIn ?? (object)DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text },
                db.Issue.Id,
            ],
            TestContext.Current.CancellationToken);

    private static async Task<(string Status, Guid? ClaimedBy)> ReadAsync(string connectionString, Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "select status, claimed_by from issue_read where id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "the issue is absent from issue_read");

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}
