using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Infrastructure;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class WakeUpTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issue_changes_wake_only_their_project_and_a_deadline_remains_the_fallback()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await using var changes = new PostgresChanges(db.ConnectionString, NullLogger<PostgresChanges>.Instance);

        await EstablishListenerAsync(changes, db);

        var other = Project.Create("OTHER", "Another project", db.User.Id, Migrated.Now);
        var otherIssue = Issue.Create(other.Id, 1, "Elsewhere", db.User.Id, Migrated.Now);
        db.Context.AddRange(other, otherIssue);
        await db.Context.SaveChangesAsync(Ct);

        using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => changes.WaitAsync(db.Project.Id, deadline.Token));
        }

        var firstWaiter = changes.WaitAsync(db.Project.Id, Ct);
        var secondWaiter = changes.WaitAsync(db.Project.Id, Ct);
        await db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"update issue set updated_at = now() where id = {db.Issue.Id}", Ct);
        await Task.WhenAll(firstWaiter, secondWaiter).WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task Inserting_and_answering_a_question_wake_the_issue_project()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await using var changes = new PostgresChanges(db.ConnectionString, NullLogger<PostgresChanges>.Instance);
        await EstablishListenerAsync(changes, db);

        var inserted = changes.WaitAsync(db.Project.Id, Ct);
        var question = Question.Ask(db.Project.Id, db.Issue.Id, "Which way?", db.Agent.Id, Migrated.Now);
        db.Context.Questions.Add(question);
        await db.Context.SaveChangesAsync(Ct);
        await inserted.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var answered = changes.WaitAsync(db.Project.Id, Ct);
        question.AnswerWith("This way.", db.User.Id, Migrated.Now.AddMinutes(1));
        await db.Context.SaveChangesAsync(Ct);
        await answered.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }

    [Fact]
    public async Task A_disconnected_listener_wakes_waiters_and_reconnects()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        await using var changes = new PostgresChanges(db.ConnectionString, NullLogger<PostgresChanges>.Instance);
        await EstablishListenerAsync(changes, db);

        var disconnected = changes.WaitAsync(db.Project.Id, Ct);
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("""
                select coalesce(bool_or(pg_terminate_backend(pid)), false)
                  from pg_stat_activity
                 where application_name = 'hostingaffe-change-listener'
                   and datname = current_database()
                """, connection);
            Assert.True((bool?)await command.ExecuteScalarAsync(Ct));
        }
        await disconnected.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        await EstablishListenerAsync(changes, db);
    }

    private static async Task EstablishListenerAsync(PostgresChanges changes, Migrated db)
    {
        var waiting = changes.WaitAsync(db.Project.Id, Ct);
        while (!waiting.IsCompleted)
        {
            await db.Context.Database.ExecuteSqlInterpolatedAsync(
                $"update issue set updated_at = now() where id = {db.Issue.Id}", Ct);
            await Task.WhenAny(waiting, Task.Delay(25, Ct));
        }

        await waiting;
    }
}
