using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Hostingaffe.Api.Http;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The two decisions a browser write rests on: the CSRF origin check, and who
/// may speak for the caller (<c>docs/operations.md</c>).
/// </summary>
public sealed class CsrfProtectionTests
{
    private static HttpRequest Request(string? origin, string host, bool header = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        if (header) context.Request.Headers[CsrfProtection.Header] = "1";
        if (origin is not null) context.Request.Headers.Origin = origin;
        return context.Request;
    }

    [Fact]
    public void A_configured_public_url_is_compared_whole()
    {
        var configured = new Uri("https://plan.example.org");
        Assert.True(CsrfProtection.IsSafe(Request("https://plan.example.org", "plan.example.org"), configured));
        Assert.False(CsrfProtection.IsSafe(Request("http://plan.example.org", "plan.example.org"), configured));
        Assert.False(CsrfProtection.IsSafe(Request("https://attacker.example", "plan.example.org"), configured));
    }

    /// <summary>
    /// Without one, the scheme is left out. A proxy that terminates TLS forwards
    /// the request as <c>http</c> unless it is trusted to say otherwise, and
    /// comparing that against the browser's <c>https</c> refused every write.
    /// </summary>
    [Fact]
    public void Without_one_the_host_carries_the_check()
    {
        Assert.True(CsrfProtection.IsSafe(Request("https://plan.example.org", "plan.example.org"), null));
        Assert.True(CsrfProtection.IsSafe(Request("http://localhost:5173", "localhost:5173"), null));
        Assert.False(CsrfProtection.IsSafe(Request("https://attacker.example", "plan.example.org"), null));
        Assert.False(CsrfProtection.IsSafe(Request("https://plan.example.org:8443", "plan.example.org"), null));
    }

    [Fact]
    public void Neither_the_header_nor_the_origin_may_be_missing()
    {
        Assert.False(CsrfProtection.IsSafe(Request("https://plan.example.org", "plan.example.org", header: false), null));
        Assert.False(CsrfProtection.IsSafe(Request(null, "plan.example.org"), null));
        Assert.False(CsrfProtection.IsSafe(Request("null", "plan.example.org"), null));
    }
}

/// <inheritdoc cref="CsrfProtectionTests"/>
public sealed class TrustedProxiesTests
{
    [Fact]
    public void Nothing_is_trusted_until_an_operator_says_so()
    {
        Assert.False(TrustedProxies.FromVariable(null).Configured);
        Assert.False(TrustedProxies.FromVariable("   ").Configured);
    }

    [Fact]
    public void Addresses_networks_and_all_are_the_three_things_it_takes()
    {
        var named = TrustedProxies.FromVariable(" 10.0.0.7 , 172.18.0.0/16 ");
        Assert.True(named.Configured);
        Assert.False(named.Any);
        Assert.Equal("10.0.0.7", Assert.Single(named.Addresses).ToString());
        Assert.Equal("172.18.0.0/16", Assert.Single(named.Networks).ToString());

        Assert.True(TrustedProxies.FromVariable("all").Any);
        Assert.True(TrustedProxies.FromVariable("ALL").Any);
    }

    [Fact]
    public void A_value_that_is_neither_stops_the_start()
    {
        Assert.Throws<ArgumentException>(() => TrustedProxies.FromVariable("not-an-address"));
        Assert.Throws<ArgumentException>(() => TrustedProxies.FromVariable("10.0.0.0/notanumber"));
    }

    /// <summary>`all` keeps both lists empty, which is what makes the middleware skip its peer check.</summary>
    [Fact]
    public void The_options_carry_the_scheme_the_address_and_one_hop()
    {
        var options = TrustedProxies.FromVariable("all").Options();
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);

        var named = TrustedProxies.FromVariable("10.0.0.7").Options();
        Assert.Equal("10.0.0.7", Assert.Single(named.KnownProxies).ToString());
    }
}

/// <summary>The fallback over HTTP, which no test reached while it was broken.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class CsrfWithoutAPublicUrlTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cookie_write_is_judged_by_its_host_alone()
    {
        await using var instance = await AnInstance.ConfiguredAsync(postgres, new Dictionary<string, string?>
        {
            ["HOSTINGAFFE_PUBLIC_URL"] = string.Empty,
            ["HOSTINGAFFE_SMTP_HOST"] = string.Empty,
            ["HOSTINGAFFE_SMTP_PORT"] = string.Empty,
            ["HOSTINGAFFE_SMTP_SECURITY"] = string.Empty,
            ["HOSTINGAFFE_SMTP_FROM_ADDRESS"] = string.Empty,
        });
        using var client = instance.ClientWith(null);
        using var exchange = await client.PostAsJsonAsync("/api/session/bootstrap",
            new { token = AnInstance.BootstrapToken, password = "a long first password" }, Ct);
        var cookie = exchange.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        // The request reaches the instance as http; the browser is on https.
        Assert.Equal(HttpStatusCode.NoContent, (await WriteAsync(client, cookie, "https://localhost")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WriteAsync(client, cookie, "https://attacker.example")).StatusCode);
    }

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string cookie, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/password")
        {
            Content = JsonContent.Create(new { current_password = "a long first password", password = "a long second password" }),
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", origin);
        request.Headers.Add(CsrfProtection.Header, "1");
        return await client.SendAsync(request, Ct);
    }
}

/// <summary>
/// The per-address limit on failed sign-ins, which is only a limit if addresses
/// are told apart. Behind a proxy every request carries the proxy's address, so
/// twenty bad passwords by anybody stopped everybody until the window passed.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class LoginThrottleAddressTests(PostgresFixture postgres)
{
    private const string Password = "a long first password";
    private const int AddressLimit = 20;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_untrusted_proxy_makes_the_limit_instance_wide()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = await SignedUpAsync(instance);

        // Twenty accounts nobody has, all arriving on one socket.
        for (var attempt = 0; attempt < AddressLimit; attempt++)
        {
            await FailAsync(client, attempt, forwardedFor: null);
        }

        using var locked = await SignInAsync(client, "maintainer@example.test", Password, forwardedFor: null);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task A_trusted_proxy_gives_every_caller_their_own_limit()
    {
        await using var instance = await AnInstance.ConfiguredAsync(
            postgres, new Dictionary<string, string?> { [TrustedProxies.Variable] = TrustedProxies.Anything });
        using var client = await SignedUpAsync(instance);

        for (var attempt = 0; attempt < AddressLimit; attempt++)
        {
            await FailAsync(client, attempt, forwardedFor: $"203.0.113.{attempt + 1}");
        }

        using var admitted = await SignInAsync(client, "maintainer@example.test", Password, forwardedFor: "198.51.100.7");
        Assert.Equal(HttpStatusCode.NoContent, admitted.StatusCode);
    }

    private static async Task<HttpClient> SignedUpAsync(AnInstance instance)
    {
        var client = instance.ClientWith(null);
        using var exchange = await client.PostAsJsonAsync("/api/session/bootstrap",
            new { token = AnInstance.BootstrapToken, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, exchange.StatusCode);
        return client;
    }

    private static async Task FailAsync(HttpClient client, int attempt, string? forwardedFor)
    {
        using var refused = await SignInAsync(client, $"nobody{attempt}@example.test", "not the password", forwardedFor);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string password, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/session")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, Ct);
    }
}

/// <summary>
/// Which cookie a session travels in, which is decided by the scheme the request
/// arrived under and not by the environment the instance runs in.
/// </summary>
/// <remarks>
/// <c>docs/install.md</c> has the first sign-in happen over
/// <c>http://&lt;host&gt;:8080/</c>, and a production instance that answered it
/// with a <c>__Host-</c> cookie was answering with a cookie the browser stores
/// nowhere: the exchange returned <c>204</c> and the browser kept nothing.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SessionCookieTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://localhost", BrowserCookie.PlainName, false)]
    [InlineData("https://localhost", BrowserCookie.SecureName, true)]
    public async Task The_scheme_of_the_request_decides_the_cookie(string origin, string name, bool secure)
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.CreateClient(new() { BaseAddress = new Uri(origin) });

        using var exchange = await client.PostAsJsonAsync("/api/session/bootstrap",
            new { token = AnInstance.BootstrapToken, password = "a long first password" }, Ct);

        var set = exchange.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith($"{name}=", set, StringComparison.Ordinal);
        Assert.Equal(secure, set.Contains("secure", StringComparison.OrdinalIgnoreCase));

        // And the cookie it set is the one it reads back: a name the door does
        // not look under admits nobody, which is the failure this guards.
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        me.Headers.Add("Cookie", set.Split(';')[0]);
        using var admitted = await client.SendAsync(me, Ct);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    /// <summary>Signing out takes the other scheme's cookie with it.</summary>
    [Fact]
    public async Task Signing_out_forgets_both_names()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
        using var exchange = await client.PostAsJsonAsync("/api/session/bootstrap",
            new { token = AnInstance.BootstrapToken, password = "a long first password" }, Ct);

        using var out_ = new HttpRequestMessage(HttpMethod.Delete, "/api/session");
        out_.Headers.Add("Cookie", exchange.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        // The origin the development environment's public URL names, which is what the CSRF guard compares against.
        out_.Headers.Add("Origin", "http://localhost:5173");
        out_.Headers.Add(CsrfProtection.Header, "1");
        using var signedOut = await client.SendAsync(out_, Ct);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        var cleared = signedOut.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cleared, value => value.StartsWith($"{BrowserCookie.PlainName}=", StringComparison.Ordinal));
        Assert.Contains(cleared, value => value.StartsWith($"{BrowserCookie.SecureName}=", StringComparison.Ordinal));
    }
}
