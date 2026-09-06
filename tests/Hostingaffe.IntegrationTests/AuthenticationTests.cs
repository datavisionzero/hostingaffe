using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The door refusing: no token, an unknown one, a revoked one — each with the
/// problem document <c>docs/api.md</c> says, and nothing that tells them apart.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AuthenticationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task No_token_is_unauthenticated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(null);

        await Unauthenticated(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_token_is_unauthenticated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(TokenSecret.Generate());

        await Unauthenticated(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_revoked_token_is_unauthenticated_from_the_next_request_on()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(AnInstance.BootstrapToken);

        using var before = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await using (var context = Migrated.ContextFor(instance.ConnectionString))
        {
            var token = await context.Tokens.SingleAsync(TestContext.Current.CancellationToken);
            token.Revoke(Migrated.Now);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Unauthenticated(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_under_another_scheme_is_unauthenticated()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Basic {AnInstance.BootstrapToken}");

        await Unauthenticated(await client.GetAsync("/api/me", TestContext.Current.CancellationToken));
    }

    private static async Task Unauthenticated(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            Assert.Equal("/problems/unauthenticated", problem.GetProperty("type").GetString());
            Assert.Equal(401, problem.GetProperty("status").GetInt32());
            Assert.Equal("/api/me", problem.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        }
    }
}
