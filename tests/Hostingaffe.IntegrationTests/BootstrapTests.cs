using System.Net;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The bootstrap's three answers against a real instance (<c>docs/storage.md</c>,
/// Bootstrap): once, ignored afterwards, and refused before anything is written
/// when the secret is too short.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BootstrapTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_second_start_ignores_the_environment()
    {
        await using var first = await AnInstance.BootstrappedAsync(postgres);
        using (first.CreateClient())
        {
            // Started: the first administrator exists.
        }

        // The operator changes both variables and starts again. Nothing moves:
        // the old token still opens the door and the new one never existed.
        await using var second = first.StartedAgain("somebody-else", "another-secret-of-more-than-thirty-two-characters");

        using var oldToken = second.ClientWith(AnInstance.BootstrapToken);
        using var stillAdmitted = await oldToken.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillAdmitted.StatusCode);

        using var newToken = second.ClientWith("another-secret-of-more-than-thirty-two-characters");
        using var refused = await newToken.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        await using var context = Migrated.ContextFor(second.ConnectionString);
        Assert.Equal(1, await context.Identities.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_short_secret_refuses_the_start_before_anything_is_written()
    {
        await using var instance = await AnInstance.StartedAsync(postgres, AnInstance.Administrator, "too-short");

        var refusal = Assert.Throws<BootstrapRefusedException>(() => instance.CreateClient());
        Assert.Contains("HOSTINGAFFE_BOOTSTRAP_TOKEN", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("32", refusal.Message, StringComparison.Ordinal);

        await using var context = Migrated.ContextFor(instance.ConnectionString);
        Assert.False(await context.Identities.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_the_variables_the_instance_starts_and_nobody_can_authenticate()
    {
        await using var instance = await AnInstance.StartedAsync(postgres, null, null);

        using var client = instance.ClientWith(AnInstance.BootstrapToken);
        using var refused = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using var version = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
    }

    [Fact]
    public async Task Half_the_variables_are_none_of_them()
    {
        await using var instance = await AnInstance.StartedAsync(postgres, AnInstance.Administrator, null);
        using var client = instance.CreateClient();

        await using var context = Migrated.ContextFor(instance.ConnectionString);
        Assert.False(await context.Identities.AnyAsync(TestContext.Current.CancellationToken));
    }
}
