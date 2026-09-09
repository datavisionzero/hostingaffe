using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The device login of ADR 0005, end to end: what `ha login` does on a machine
/// with no browser, and what the screen that approves it does.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DeviceLoginEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_begun_login_is_approved_by_a_user_and_the_token_is_collected_once()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anonymous = instance.ClientWith(null);

        var begun = await Begun(anonymous);
        var deviceCode = begun.GetProperty("device_code").GetString()!;
        var userCode = begun.GetProperty("user_code").GetString()!;

        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", userCode);
        Assert.Equal("/device", begun.GetProperty("verification_uri").GetString());
        Assert.Equal($"/device?code={userCode}", begun.GetProperty("verification_uri_complete").GetString());
        Assert.Equal(600, begun.GetProperty("expires_in_seconds").GetInt32());
        Assert.Equal(5, begun.GetProperty("interval_seconds").GetInt32());

        // Nothing yet: the CLI polls on exactly this code and stops on any other.
        await Polled(anonymous, deviceCode, HttpStatusCode.BadRequest, "device-pending");

        // The screen reads what is waiting, and then approves it.
        using var user = instance.ClientWith(AnInstance.BootstrapToken);
        using var waiting = await user.GetAsync($"/api/device/logins/{userCode}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
        Assert.Equal("pending", (await Read(waiting)).GetProperty("state").GetString());

        using var approved = await user.PostAsJsonAsync(
            "/api/device/approvals", new { user_code = userCode }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("approved", (await Read(approved)).GetProperty("state").GetString());

        using var collected = await anonymous.PostAsJsonAsync(
            "/api/device/tokens", new { device_code = deviceCode }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
        var session = await Read(collected);
        Assert.Equal(AnInstance.Administrator, session.GetProperty("user").GetProperty("name").GetString());

        // The secret admits, and it is the caller the approval named.
        var secret = session.GetProperty("token").GetProperty("secret").GetString()!;
        using var signedIn = instance.ClientWith(secret);
        using var me = await signedIn.GetAsync("/api/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(AnInstance.Administrator, (await Read(me)).GetProperty("name").GetString());

        // A device code works once: one left in a CI log is worth nothing.
        await Polled(anonymous, deviceCode, HttpStatusCode.BadRequest, "device-expired");
    }

    [Fact]
    public async Task A_refused_login_is_told_so_and_hands_nothing_over()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anonymous = instance.ClientWith(null);
        using var user = instance.ClientWith(AnInstance.BootstrapToken);

        var begun = await Begun(anonymous);
        var deviceCode = begun.GetProperty("device_code").GetString()!;
        var userCode = begun.GetProperty("user_code").GetString()!;

        using var refused = await user.PostAsJsonAsync(
            "/api/device/refusals", new { user_code = userCode }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);

        await Polled(anonymous, deviceCode, HttpStatusCode.BadRequest, "device-denied");

        // And a second answer on a login already dealt with says so rather than
        // silently changing it.
        using var again = await user.PostAsJsonAsync(
            "/api/device/approvals", new { user_code = userCode }, TestContext.Current.CancellationToken);
        await Refusals.Problem(again, HttpStatusCode.BadRequest, "device-denied");
    }

    [Fact]
    public async Task An_agent_administers_no_identities_and_approves_no_login()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anonymous = instance.ClientWith(null);
        using var user = instance.ClientWith(AnInstance.BootstrapToken);

        using var created = await user.PostAsJsonAsync(
            "/api/agents", new { name = "runner" }, TestContext.Current.CancellationToken);
        var agentToken = (await Read(created)).GetProperty("token").GetProperty("secret").GetString()!;

        var begun = await Begun(anonymous);
        var userCode = begun.GetProperty("user_code").GetString()!;

        using var agent = instance.ClientWith(agentToken);
        using var forbidden = await agent.PostAsJsonAsync(
            "/api/device/approvals", new { user_code = userCode }, TestContext.Current.CancellationToken);
        await Refusals.Problem(forbidden, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_code_nobody_began_is_not_found_whichever_end_asks()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anonymous = instance.ClientWith(null);
        using var user = instance.ClientWith(AnInstance.BootstrapToken);

        using var unknown = await user.GetAsync("/api/device/logins/BCDF-GHJK", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var nonsense = await user.PostAsJsonAsync(
            "/api/device/approvals", new { user_code = "not a code" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, nonsense.StatusCode);

        await Polled(anonymous, "not-a-device-code", HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task Beginning_and_polling_need_no_token_and_reading_one_does()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var anonymous = instance.ClientWith(null);

        var begun = await Begun(anonymous);

        using var unauthenticated = await anonymous.GetAsync(
            $"/api/device/logins/{begun.GetProperty("user_code").GetString()}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    private static async Task<JsonElement> Begun(HttpClient client)
    {
        using var response = await client.PostAsync("/api/device/logins", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read(response);
    }

    /// <summary>
    /// A poll, and the refusal it came back with — which is the whole protocol:
    /// `device-pending` means keep asking, and every other code means stop.
    /// </summary>
    private static async Task Polled(HttpClient client, string deviceCode, HttpStatusCode status, string code) =>
        await Refusals.Problem(
            await client.PostAsJsonAsync("/api/device/tokens", new { device_code = deviceCode }, TestContext.Current.CancellationToken),
            status,
            code);

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
}
