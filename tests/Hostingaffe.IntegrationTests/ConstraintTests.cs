using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// One test per constraint the database holds (<c>docs/storage.md</c>), each
/// showing the database refusing the state. The writes are SQL on purpose: the
/// Domain types cannot produce these states, and the point is that the last
/// line holds when something else does — a hand-written update, a bug in a
/// store, a migration.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ConstraintTests(PostgresFixture postgres)
{
    // -- identity --------------------------------------------------------------

    [Fact]
    public async Task An_agent_is_never_an_administrator()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_identity_owner", db.Context,
            "update identity set administrator = true where id = {0}", db.Agent.Id);
    }

    [Fact]
    public async Task An_agent_has_an_owner()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_identity_owner", db.Context,
            "update identity set owner_id = null where id = {0}", db.Agent.Id);
    }

    [Fact]
    public async Task A_user_has_no_owner()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_identity_owner", db.Context,
            "update identity set owner_id = {0} where id = {0}", db.User.Id);
    }

    [Fact]
    public async Task An_identity_is_a_user_or_an_agent()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_identity_kind", db.Context,
            "update identity set kind = 'robot' where id = {0}", db.Agent.Id);
    }

    [Fact]
    public async Task Names_are_unique_across_both_kinds_and_regardless_of_case()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // The agent is `quiet-otter-42`; a user by that name, in any case, is
        // the same address and refused.
        db.Context.Users.Add(User.Create("Quiet-Otter-42", administrator: false, Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("identity_name", Assert.IsType<PostgresException>(refusal.InnerException).ConstraintName);
    }

    // -- token -----------------------------------------------------------------

    [Fact]
    public async Task A_token_is_a_users_or_an_agents()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        db.Context.Tokens.Add(Token.Issue(db.Agent, "ha_abcde", Migrated.Hash("one"), Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Refused("ck_token_kind", db.Context,
            "update token set kind = 'session' where identity_id = {0}", db.Agent.Id);
    }

    [Fact]
    public async Task An_agent_has_exactly_one_token()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        db.Context.Tokens.Add(Token.Issue(db.Agent, "ha_abcde", Migrated.Hash("one"), Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Context.Tokens.Add(Token.Issue(db.Agent, "ha_fghij", Migrated.Hash("two"), Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("token_agent", Assert.IsType<PostgresException>(refusal.InnerException).ConstraintName);
    }

    [Fact]
    public async Task A_user_has_as_many_tokens_as_they_create()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        db.Context.Tokens.Add(Token.Issue(db.User, "ha_abcde", Migrated.Hash("one"), Migrated.Now));
        db.Context.Tokens.Add(Token.Issue(db.User, "ha_fghij", Migrated.Hash("two"), Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = db.Reader();
        Assert.Equal(2, await reader.Tokens.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_tokens_cannot_share_a_secret()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        db.Context.Tokens.Add(Token.Issue(db.User, "ha_abcde", Migrated.Hash("same"), Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Context.Tokens.Add(Token.Issue(db.Agent, "ha_abcde", Migrated.Hash("same"), Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("token_secret_hash", Assert.IsType<PostgresException>(refusal.InnerException).ConstraintName);
    }

    // -- history ---------------------------------------------------------------

    [Fact]
    public async Task The_history_is_numbered_by_the_database_alone()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // `generated always`: a caller that brings its own id is refused, so the
        // order of the ids is the order the rows were written and nothing else.
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Context.Database.ExecuteSqlRawAsync(
                "insert into history (id, subject, subject_id, actor_id, at, field) values (7, 'page', {0}, {1}, now(), 'created')",
                [db.Page.Id, db.User.Id],
                TestContext.Current.CancellationToken));

        // SQLSTATE 428C9, `generated_always`, which Npgsql has no constant for.
        Assert.Equal("428C9", refusal.SqlState);
    }

    // -- machine ---------------------------------------------------------------

    [Theory]
    [InlineData("ck_machine_kind", "kind = 'toaster'")]
    [InlineData("ck_machine_arch", "arch = 'sparc'")]
    [InlineData("ck_machine_status", "status = 'broken'")]
    public async Task A_closed_set_is_closed_in_the_column_too(string constraint, string assignment)
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused(constraint, db.Context, $"update machine set {assignment} where id = {{0}}", db.Machine.Id);
    }

    [Fact]
    public async Task Only_a_vm_names_a_host()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // The seeded machine is dedicated, so a host on it is a state the model
        // does not have — and the column says so, not only the write path.
        await Refused("ck_machine_host", db.Context,
            "update machine set host_id = {0} where id = {0}", db.Machine.Id);
    }

    [Fact]
    public async Task No_machine_runs_on_itself()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_machine_not_its_own_host", db.Context,
            "update machine set kind = 'vm', host_id = {0} where id = {0}", db.Machine.Id);
    }

    [Fact]
    public async Task A_history_row_names_a_subject_the_code_knows()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_history_subject", db.Context,
            "insert into history (subject, subject_id, actor_id, at, field) values ('installation', {0}, {1}, now(), 'created')",
            db.Machine.Id, db.User.Id);
    }

    private static async Task Refused(
        string constraint, DbContext context, string sql, params object[] parameters)
    {
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(sql, parameters, TestContext.Current.CancellationToken));

        Assert.Equal(constraint, refusal.ConstraintName);
    }
}
