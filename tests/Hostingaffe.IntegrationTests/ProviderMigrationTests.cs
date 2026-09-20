using Hostingaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ProviderMigrationTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    [Fact]
    public async Task Existing_provider_text_and_installations_survive_the_upgrade()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var options = new DbContextOptionsBuilder<HostingaffeDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var context = new HostingaffeDbContext(options);
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260919115529_TheHistoryIsAlsoReadByTheMoment", Ct);

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(Ct);
            await using var seed = new NpgsqlCommand("""
                INSERT INTO identity (id, name, kind, administrator, created_at,
                                      email, normalized_email, user_state)
                VALUES ('00000000-0000-0000-0000-000000000001', 'Example User', 'user', true,
                        '2026-09-01T00:00:00Z', 'operator@example.test', 'operator@example.test', 'active');
                INSERT INTO software (id, key, name, created_by, created_at, updated_by, updated_at)
                VALUES ('00000000-0000-0000-0000-000000000002', 'example-app', 'Example App',
                        '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z',
                        '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z');
                INSERT INTO machine (id, key, name, kind, host_id, provider, status,
                                     created_by, created_at, updated_by, updated_at)
                VALUES
                ('00000000-0000-0000-0000-000000000011', 'host', 'Host', 'vps', null, 'hetzner', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z'),
                ('00000000-0000-0000-0000-000000000012', 'second', 'Second', 'vps', null, 'hetzner', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z'),
                ('00000000-0000-0000-0000-000000000013', 'mixed', 'Mixed', 'vps', null, 'Hetzner', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z'),
                ('00000000-0000-0000-0000-000000000014', 'punctuation', 'Punctuation', 'vps', null, '!!', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z'),
                ('00000000-0000-0000-0000-000000000015', 'blank', 'Blank', 'local', null, '   ', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z'),
                ('00000000-0000-0000-0000-000000000016', 'guest', 'Guest', 'vm', '00000000-0000-0000-0000-000000000011', 'other.host', 'active',
                 '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z', '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z');
                INSERT INTO installation (id, key, name, machine_id, software_id, environment, role, status,
                                          backup, monitoring, logging, created_by, created_at, updated_by, updated_at)
                VALUES ('00000000-0000-0000-0000-000000000021', 'example-app-host', 'Example App',
                        '00000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000002',
                        'production', 'application', 'active', 'none', 'none', 'local',
                        '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z',
                        '00000000-0000-0000-0000-000000000001', '2026-09-01T00:00:00Z');
                """, connection);
            await seed.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(cancellationToken: Ct);

        await using var check = new NpgsqlConnection(connectionString);
        await check.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("""
            SELECT m.key, m.provider, m.legacy_provider FROM machine m ORDER BY m.key;
            SELECT name, key FROM provider ORDER BY name COLLATE "C";
            SELECT count(*) FROM installation WHERE key = 'example-app-host';
            """, check);
        await using var rows = await command.ExecuteReaderAsync(Ct);
        var machines = new Dictionary<string, (string? Provider, string? Legacy)>();
        while (await rows.ReadAsync(Ct))
        {
            machines.Add(rows.GetString(0), (rows.IsDBNull(1) ? null : rows.GetString(1),
                                            rows.IsDBNull(2) ? null : rows.GetString(2)));
        }

        Assert.Equal((null, "   "), machines["blank"]);
        Assert.Equal((null, "other.host"), machines["guest"]);
        Assert.Equal(("provider", "!!"), machines["punctuation"]);
        Assert.Equal(machines["host"], machines["second"]);
        Assert.NotEqual(machines["host"].Provider, machines["mixed"].Provider);

        await rows.NextResultAsync(Ct);
        var providers = new Dictionary<string, string>();
        while (await rows.ReadAsync(Ct)) providers.Add(rows.GetString(0), rows.GetString(1));
        Assert.Equal(4, providers.Count);
        Assert.Equal("hetzner", providers["Hetzner"]);
        Assert.Equal("hetzner-2", providers["hetzner"]);
        Assert.Equal("other-host", providers["other.host"]);

        await rows.NextResultAsync(Ct);
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal(1L, rows.GetInt64(0));

        var machineStore = new Machines(context);
        var effective = await machineStore.ProviderKeysAsync(
            [Guid.Parse("00000000-0000-0000-0000-000000000011"),
             Guid.Parse("00000000-0000-0000-0000-000000000016")], Ct);
        Assert.Equal(providers["hetzner"], effective[Guid.Parse("00000000-0000-0000-0000-000000000016")]);

        await using var enforce = new NpgsqlConnection(connectionString);
        await enforce.OpenAsync(Ct);
        await using var invalid = new NpgsqlCommand(
            "UPDATE machine SET provider = 'does-not-exist' WHERE key = 'host'", enforce);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(
            () => invalid.ExecuteNonQueryAsync(Ct))).SqlState);

        await using var independentVm = new NpgsqlCommand(
            "UPDATE machine SET provider = 'hetzner' WHERE key = 'guest'", enforce);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(
            () => independentVm.ExecuteNonQueryAsync(Ct))).SqlState);

        await using var move = new NpgsqlCommand(
            "UPDATE machine SET host_id = '00000000-0000-0000-0000-000000000013' WHERE key = 'guest'", enforce);
        await move.ExecuteNonQueryAsync(Ct);
        effective = await machineStore.ProviderKeysAsync(
            [Guid.Parse("00000000-0000-0000-0000-000000000016")], Ct);
        Assert.Equal(providers["Hetzner"], effective[Guid.Parse("00000000-0000-0000-0000-000000000016")]);
    }
}
