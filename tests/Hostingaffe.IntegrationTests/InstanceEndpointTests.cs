using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Api.Hosting;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The first vertical slice, end to end: the instance bootstraps itself from
/// the environment, the bootstrap token opens the door, and <c>/me</c> says who
/// came through it (PLAN-0016).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InstanceEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_bootstrap_token_authenticates_the_first_administrator()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);

        using var response = await client.GetAsync("/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("user", me.GetProperty("kind").GetString());
        Assert.Equal(AnInstance.Administrator, me.GetProperty("name").GetString());
        Assert.True(me.GetProperty("administrator").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("owner").ValueKind);
        Assert.Equal(AnInstance.BootstrapToken[..8], me.GetProperty("token").GetProperty("prefix").GetString());
        Assert.True(me.GetProperty("token").TryGetProperty("created_at", out _));
    }

    [Fact]
    public async Task An_agent_reads_itself_with_its_owner()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        var secret = TokenSecret.Generate();

        // The host starts — migrates and bootstraps — on the first client, so
        // the client comes before the rows this test adds beside the bootstrap's.
        using var client = instance.ClientWith(secret);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var owner = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
            var agent = Agent.Create("quiet-otter-42", owner.Id, Migrated.Now);
            context.Agents.Add(agent);
            context.Tokens.Add(Token.Issue(agent, secret, Migrated.Now));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var me = await client.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        Assert.Equal("agent", me.GetProperty("kind").GetString());
        Assert.Equal("quiet-otter-42", me.GetProperty("name").GetString());
        Assert.False(me.GetProperty("administrator").GetBoolean());
        Assert.Equal(AnInstance.Administrator, me.GetProperty("owner").GetProperty("name").GetString());
        Assert.Equal("user", me.GetProperty("owner").GetProperty("kind").GetString());
        Assert.Equal("ha_", me.GetProperty("token").GetProperty("prefix").GetString()![..3]);
    }

    [Fact]
    public async Task The_version_needs_no_token_and_is_on_every_response()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(null);

        using var version = await client.GetAsync("/version", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);

        var body = await version.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(InstanceVersion.Value, body.GetProperty("version").GetString());
        Assert.Equal(InstanceVersion.Value, Assert.Single(version.Headers.GetValues("Hostingaffe-Version")));

        // A refusal is an answer too, and the CLI reports skew from whatever it got.
        using var refused = await client.GetAsync("/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(InstanceVersion.Value, Assert.Single(refused.Headers.GetValues("Hostingaffe-Version")));
    }

    [Fact]
    public void The_version_is_a_semver_without_build_metadata()
    {
        Assert.DoesNotContain('+', InstanceVersion.Value);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", InstanceVersion.Value);
    }
}
