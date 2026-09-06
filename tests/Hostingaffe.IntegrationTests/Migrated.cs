using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;
using Hostingaffe.Domain.Projects;
using Hostingaffe.Infrastructure.Persistence;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// A migrated database with the rows most tests need to say anything: a user,
/// an agent the user owns, a project, and one issue in it.
/// </summary>
internal sealed class Migrated(string connectionString) : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public string ConnectionString { get; } = connectionString;

    public HostingaffeDbContext Context { get; } = ContextFor(connectionString);

    public User User { get; private set; } = null!;

    public Agent Agent { get; private set; } = null!;

    public Project Project { get; private set; } = null!;

    public Issue Issue { get; private set; } = null!;

    public static async Task<Migrated> EmptyAsync(PostgresFixture postgres)
    {
        var migrated = new Migrated(await postgres.CreateDatabaseAsync());
        await MigratorFor(migrated.Context).ApplyAsync(TestContext.Current.CancellationToken);
        return migrated;
    }

    public static async Task<Migrated> SeededAsync(PostgresFixture postgres)
    {
        var migrated = await EmptyAsync(postgres);
        var context = migrated.Context;

        migrated.User = User.Create("maintainer", administrator: true, Now);
        migrated.Agent = Agent.Create("quiet-otter-42", migrated.User.Id, Now);
        migrated.Project = Project.Create("PLAN", "hostingaffe", migrated.User.Id, Now);
        migrated.Issue = Issue.Create(migrated.Project.Id, 1, "Write the schema", migrated.User.Id, Now);

        context.AddRange(migrated.User, migrated.Agent, migrated.Project, migrated.Issue);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        return migrated;
    }

    /// <summary>A fresh context on the same database, so that a read is a read and not the tracker.</summary>
    public HostingaffeDbContext Reader() => ContextFor(ConnectionString);

    public static HostingaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<HostingaffeDbContext>().UseNpgsql(connectionString).Options);

    public static SchemaMigrator MigratorFor(HostingaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);

    /// <summary>
    /// Stands in for what the token path will hash. The schema's job is to hold
    /// thirty-two bytes and find the row by them.
    /// </summary>
    public static byte[] Hash(string secret) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
