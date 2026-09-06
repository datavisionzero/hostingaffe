using Microsoft.EntityFrameworkCore;
using Npgsql;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Pages;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// What the database holds about a page and no substitute could vouch for
/// (<c>docs/storage.md</c>, Pages): the slug is unique within the project even
/// when two creators race for it, it stays spent while the page is deleted, and
/// the purge is what gives it back.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PageSchemaTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_creators_of_one_slug_produce_exactly_one_page()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        // Twenty writers at once, each committing a transaction of its own.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = Migrated.ContextFor(db.ConnectionString);
            try
            {
                context.Pages.Add(Page.Create(db.Project.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (Unique(exception))
            {
                return false;
            }
        }));

        Assert.Equal(1, outcomes.Count(won => won));

        await using var reader = db.Reader();
        Assert.Equal(1, await reader.Pages.CountAsync(p => p.Slug == "architecture", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_projects_may_both_have_architecture()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var other = Domain.Projects.Project.Create("LOG", "logaffe", db.User.Id, Migrated.Now);
        db.Context.Projects.Add(other);

        db.Context.Pages.Add(Page.Create(db.Project.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now));
        db.Context.Pages.Add(Page.Create(other.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = db.Reader();
        Assert.Equal(2, await reader.Pages.CountAsync(p => p.Slug == "architecture", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The index covers deleted rows on purpose: a restore must never land on a
    /// name somebody else has taken in the meantime (ADR 0013).
    /// </summary>
    [Fact]
    public async Task A_deleted_page_keeps_its_slug_until_the_purge()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var page = Page.Create(db.Project.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now);
        page.Delete(db.User.Id, Migrated.Now);
        db.Context.Pages.Add(page);
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Context.ChangeTracker.Clear();

        db.Context.Pages.Add(Page.Create(db.Project.Id, "architecture", "Architecture again", null, db.User.Id, Migrated.Now));

        var refusal = await Assert.ThrowsAsync<DbUpdateException>(() =>
            db.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("page_slug", ((PostgresException)refusal.InnerException!).ConstraintName);
    }

    [Fact]
    public async Task The_purge_gives_the_slug_back()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var grace = TimeSpan.FromDays(7);

        var page = Page.Create(db.Project.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now);
        page.Delete(db.User.Id, DateTimeOffset.UtcNow - grace - TimeSpan.FromDays(1));
        db.Context.Pages.Add(page);
        db.Context.History.Add(Domain.History.HistoryEntry.OnPage(
            page.Id, db.User.Id, Migrated.Now, Domain.History.HistoryField.Created));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Context.ChangeTracker.Clear();

        // The purge runs at the end of any write transaction that touched the
        // project, and not before the write itself: here it is an unrelated
        // page that pays for it.
        await using (var context = Migrated.ContextFor(db.ConnectionString))
        {
            var transactions = new Transactions(context, new InstanceSettings(grace));
            await transactions.RunAsync(async () =>
            {
                context.Pages.Add(Page.Create(db.Project.Id, "onboarding", "Onboarding", null, db.User.Id, Migrated.Now));
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                return true;
            }, TestContext.Current.CancellationToken);
        }

        // The slug is free, so the same name can be taken again.
        await using (var context = Migrated.ContextFor(db.ConnectionString))
        {
            context.Pages.Add(Page.Create(db.Project.Id, "architecture", "Architecture again", null, db.User.Id, Migrated.Now));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = db.Reader();
        var remaining = await reader.Pages.SingleAsync(p => p.Slug == "architecture", TestContext.Current.CancellationToken);
        Assert.Equal("Architecture again", remaining.Title);

        // The history went with the row: it dies with its subject (ADR 0013).
        Assert.Empty(await reader.History.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_the_project_takes_its_pages()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        db.Context.Pages.Add(Page.Create(db.Project.Id, "architecture", "Architecture", null, db.User.Id, Migrated.Now));
        await db.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await db.Context.Database.ExecuteSqlRawAsync(
            "delete from project where id = {0}", [db.Project.Id], TestContext.Current.CancellationToken);

        await using var reader = db.Reader();
        Assert.Empty(await reader.Pages.ToListAsync(TestContext.Current.CancellationToken));
    }

    private static bool Unique(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
