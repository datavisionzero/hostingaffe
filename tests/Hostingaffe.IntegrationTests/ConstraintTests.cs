using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Domain.Epics;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;

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

    // -- project ---------------------------------------------------------------

    [Fact]
    public async Task A_project_key_is_taken_even_while_the_project_is_deleted()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await db.Context.Database.ExecuteSqlRawAsync(
            "update project set deleted_at = now(), deleted_by = {0} where id = {1}",
            [db.User.Id, db.Project.Id],
            TestContext.Current.CancellationToken);

        db.Context.Projects.Add(Project.Create("PLAN", "hostingaffe again", db.User.Id, Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("project_key", Assert.IsType<PostgresException>(refusal.InnerException).ConstraintName);
    }

    // -- epic ------------------------------------------------------------------

    [Fact]
    public async Task An_epic_is_closed_exactly_when_closed_at_is_set()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var epic = Epic.Create(db.Project.Id, 1, "Backend", db.User.Id, Migrated.Now);
        db.Context.Epics.Add(epic);
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Refused("ck_epic_closed", db.Context,
            "update epic set status = 'closed' where id = {0}", epic.Id);
        await Refused("ck_epic_closed", db.Context,
            "update epic set closed_at = now() where id = {0}", epic.Id);
    }

    [Fact]
    public async Task An_epic_is_open_or_closed()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var epic = Epic.Create(db.Project.Id, 1, "Backend", db.User.Id, Migrated.Now);
        db.Context.Epics.Add(epic);
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Refused("ck_epic_status", db.Context,
            "update epic set status = 'archived' where id = {0}", epic.Id);
    }

    // -- issue -----------------------------------------------------------------

    [Fact]
    public async Task In_progress_without_a_holder_is_refused()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_claimed", db.Context,
            "update issue set status = 'in_progress' where id = {0}", db.Issue.Id);
    }

    [Fact]
    public async Task A_holder_on_an_issue_not_in_progress_is_refused()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_claimed", db.Context,
            "update issue set claimed_by = {0}, claimed_at = now(), claim_extended_at = now() where id = {1}",
            db.Agent.Id, db.Issue.Id);
    }

    [Fact]
    public async Task Done_without_closed_at_is_refused()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_closed", db.Context,
            "update issue set status = 'done' where id = {0}", db.Issue.Id);
        await Refused("ck_issue_closed", db.Context,
            "update issue set status = 'canceled' where id = {0}", db.Issue.Id);
    }

    [Fact]
    public async Task Closed_at_on_an_open_issue_is_refused()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_closed", db.Context,
            "update issue set closed_at = now() where id = {0}", db.Issue.Id);
    }

    [Fact]
    public async Task The_claim_columns_come_and_go_together()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_claim_columns", db.Context,
            "update issue set status = 'in_progress', claimed_by = {0}, claim_extended_at = now() where id = {1}",
            db.Agent.Id, db.Issue.Id);
        await Refused("ck_issue_claim_columns", db.Context,
            "update issue set status = 'in_progress', claimed_by = {0}, claimed_at = now() where id = {1}",
            db.Agent.Id, db.Issue.Id);
    }

    [Fact]
    public async Task A_users_claim_has_no_expiry_and_that_is_allowed()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // The one claim column that may be null on its own: a user's claim
        // never expires (VISION 11), and the row says so with a null.
        await db.Context.Database.ExecuteSqlRawAsync(
            "update issue set status = 'in_progress', claimed_by = {0}, claimed_at = now(), claim_extended_at = now() where id = {1}",
            [db.User.Id, db.Issue.Id],
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_status_is_one_of_the_six()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_status", db.Context,
            "update issue set status = 'blocked' where id = {0}", db.Issue.Id);
    }

    [Fact]
    public async Task Priority_is_zero_to_four()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_issue_priority", db.Context,
            "update issue set priority = 5 where id = {0}", db.Issue.Id);
        await Refused("ck_issue_priority", db.Context,
            "update issue set priority = -1 where id = {0}", db.Issue.Id);
    }

    [Fact]
    public async Task Keys_are_unique_within_a_project()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        db.Context.Issues.Add(Issue.Create(db.Project.Id, 1, "A second PLAN-1", db.User.Id, Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(
            () => db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("issue_number", Assert.IsType<PostgresException>(refusal.InnerException).ConstraintName);
    }

    [Fact]
    public async Task An_issue_cannot_block_itself()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await Refused("ck_blocker_not_self", db.Context,
            "insert into blocker (blocker_id, blocked_id, created_by, created_at) values ({0}, {0}, {1}, now())",
            db.Issue.Id, db.User.Id);
    }

    [Fact]
    public async Task A_question_is_answered_whole_or_not_at_all()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var question = Question.Ask(db.Project.Id, db.Issue.Id, "Which Postgres?", db.Agent.Id, Migrated.Now);
        db.Context.Questions.Add(question);
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Refused("ck_question_answer", db.Context,
            "update question set answer = '18' where id = {0}", question.Id);
        await Refused("ck_question_answer", db.Context,
            "update question set answered_by = {0}, answered_at = now() where id = {1}", db.User.Id, question.Id);
    }

    [Fact]
    public async Task A_history_entry_has_exactly_one_subject()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var epic = Epic.Create(db.Project.Id, 1, "Backend", db.User.Id, Migrated.Now);
        db.Context.Epics.Add(epic);
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Refused("ck_history_subject", db.Context,
            "insert into history (actor_id, at, field) values ({0}, now(), 'created')", db.User.Id);
        await Refused("ck_history_subject", db.Context,
            "insert into history (issue_id, epic_id, actor_id, at, field) values ({0}, {1}, {2}, now(), 'created')",
            db.Issue.Id, epic.Id, db.User.Id);
    }

    [Fact]
    public async Task The_history_is_numbered_by_the_database_alone()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // `generated always`: a caller that brings its own id is refused, so the
        // order of the ids is the order the rows were written and nothing else.
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Context.Database.ExecuteSqlRawAsync(
                "insert into history (id, issue_id, actor_id, at, field) values (7, {0}, {1}, now(), 'created')",
                [db.Issue.Id, db.User.Id],
                TestContext.Current.CancellationToken));

        // SQLSTATE 428C9, `generated_always`, which Npgsql has no constant for.
        Assert.Equal("428C9", refusal.SqlState);
    }

    private static async Task Refused(
        string constraint, DbContext context, string sql, params object[] parameters)
    {
        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(sql, parameters, TestContext.Current.CancellationToken));

        Assert.Equal(constraint, refusal.ConstraintName);
    }
}
