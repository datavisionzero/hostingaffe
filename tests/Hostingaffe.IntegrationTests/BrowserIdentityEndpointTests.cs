using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BrowserIdentityEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Bootstrap_token_becomes_a_cookie_session_once_and_password_sign_in_works()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(null);

        using var exchange = await client.PostAsJsonAsync("/session/bootstrap", new
        {
            token = AnInstance.BootstrapToken,
            password = "a long first password",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, exchange.StatusCode);
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/me");
        meRequest.Headers.Add("Cookie", cookie);
        using var me = await client.SendAsync(meRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Null((await me.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("token").GetString());

        using var repeated = await client.PostAsJsonAsync("/session/bootstrap", new
        {
            token = AnInstance.BootstrapToken,
            password = "another long password",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, repeated.StatusCode);

        using var login = await client.PostAsJsonAsync("/session", new
        {
            email = "maintainer@example.test",
            password = "a long first password",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    [Fact]
    public async Task Cookie_writes_need_the_csrf_header_and_origin_but_bearer_writes_do_not()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(null);
        using var exchange = await client.PostAsJsonAsync("/session/bootstrap", new { token = AnInstance.BootstrapToken, password = "a long first password" }, TestContext.Current.CancellationToken);
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        using var refused = new HttpRequestMessage(HttpMethod.Delete, "/session");
        refused.Headers.Add("Cookie", cookie);
        using var refusedResponse = await client.SendAsync(refused, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refusedResponse.StatusCode);

        using var accepted = new HttpRequestMessage(HttpMethod.Delete, "/session");
        accepted.Headers.Add("Cookie", cookie); accepted.Headers.Add("Origin", "http://localhost:5173"); accepted.Headers.Add("X-Hostingaffe-CSRF", "1");
        using var acceptedResponse = await client.SendAsync(accepted, TestContext.Current.CancellationToken);
        Assert.True(acceptedResponse.StatusCode == HttpStatusCode.NoContent,
            $"{acceptedResponse.StatusCode}: {await acceptedResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
    }
}
