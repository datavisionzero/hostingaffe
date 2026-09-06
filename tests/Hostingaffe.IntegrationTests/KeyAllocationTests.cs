using Microsoft.EntityFrameworkCore;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// Keys are drawn from the project row under its lock (<c>docs/storage.md</c>,
/// Keys are allocated from the project row): concurrent creators get distinct
/// numbers with no gap between them, and a rolled-back creator gives its
/// numbers back.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class KeyAllocationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_allocations_are_distinct_and_dense()
    {
        await using var db = await Migrated.SeededAsync(postgres);
        var projectId = db.Project.Id;

        // Twenty creators at once, each taking one to three numbers in a
        // transaction of its own.
        var requests = Enumerable.Range(0, 20).Select(i => 1 + i % 3).ToArray();
        var ranges = await Task.WhenAll(requests.Select(async count =>
        {
            await using var context = Migrated.ContextFor(db.ConnectionString);
            await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            var first = await new Projects(context).AllocateIssueNumbersAsync(projectId, count, TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
            return Enumerable.Range(first, count);
        }));

        var numbers = ranges.SelectMany(r => r).Order().ToArray();
        Assert.Equal(Enumerable.Range(1, requests.Sum()), numbers);

        await using var reader = db.Reader();
        var project = await reader.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(requests.Sum(), project.LastIssueNumber);
        Assert.Equal(0, project.LastEpicNumber);
    }

    [Fact]
    public async Task A_rolled_back_allocation_leaves_no_gap()
    {
        await using var db = await Migrated.SeededAsync(postgres);

        await using (var context = Migrated.ContextFor(db.ConnectionString))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, await new Projects(context).AllocateIssueNumbersAsync(db.Project.Id, 7, TestContext.Current.CancellationToken));
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = Migrated.ContextFor(db.ConnectionString))
        {
            // The seven that were rolled back are drawn again: nothing has to
            // explain a gap.
            Assert.Equal(1, await new Projects(context).AllocateIssueNumbersAsync(db.Project.Id, 1, TestContext.Current.CancellationToken));
            Assert.Equal(1, await new Projects(context).AllocateEpicNumbersAsync(db.Project.Id, 2, TestContext.Current.CancellationToken));
            Assert.Equal(3, await new Projects(context).AllocateEpicNumbersAsync(db.Project.Id, 1, TestContext.Current.CancellationToken));
        }
    }
}
