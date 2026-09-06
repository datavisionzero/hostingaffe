using Npgsql;
using Testcontainers.PostgreSql;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// One Postgres for the whole run, and a fresh database inside it per test.
/// </summary>
/// <remarks>
/// docs/codebase.md splits the tests by what they need to run, and what this
/// project needs is a real Postgres — because the acts worth testing here are
/// the concurrent ones and the constraints, and no substitute can vouch for
/// those. The image is named rather than defaulted, and the version is the one
/// the Compose file will run: a test that passes against a different major than
/// the instance uses has proved something about neither.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// A database of its own, so that one test's schema is never another's
    /// starting point.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"hostingaffe_{Guid.NewGuid():n}"[..24];

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""CREATE DATABASE "{name}" """, connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,

            // Npgsql pools per connection string, and a database of its own is
            // a connection string of its own, so a run leaves one pool per test
            // behind. A short idle lifetime and a pruner take the connections
            // back within seconds of a test finishing, and a small ceiling
            // means one test's pool cannot be what exhausts the server.
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
            MaxPoolSize = 10,
        }.ConnectionString;
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
