using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Routing;
using Hostingaffe.Api.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hostingaffe.IntegrationTests;

/// <summary>
/// The line ADR 0002 draws: the API is everything under <c>/api</c>, and every
/// other address belongs to the web application. Before the prefix,
/// <c>GET /pages</c> from a browser — a reload, a bookmark, a pasted link —
/// was answered by the API with a problem document instead of the application,
/// and the router never noticed because it navigates client-side.
/// </summary>
/// <remarks>
/// The first test is the one that keeps holding: an endpoint mapped outside the
/// group fails it whatever it is called, which is what makes `machines` and
/// `deployments` safe to add later.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class WebApplicationAddressTests(PostgresFixture postgres)
{
    /// <summary>What the SPA fallback matches: every path the API did not take.</summary>
    private const string Fallback = "/{*path:nonfile}";

    [Fact]
    public async Task Nothing_but_the_fallback_is_mapped_outside_the_api()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);

        // The host is built on the first client, and the routes with it.
        using var client = instance.ClientWith(null);
        await client.GetAsync("/api/version", TestContext.Current.CancellationToken);

        var outside = instance.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(pattern => pattern != Routes.Api && !pattern.StartsWith($"{Routes.Api}/", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([Fallback], outside);
    }

    [Fact]
    public async Task An_address_of_the_application_is_never_answered_by_the_api()
    {
        await using var instance = await AnInstance.BootstrappedAsync(postgres);
        using var client = instance.ClientWith(null);

        // `/pages` is the collision this was found through; the others are the
        // rest of the application's own addresses, and `/machines` is one the
        // domain has not brought yet.
        foreach (var address in new[] { "/pages", "/pages/architecture", "/settings", "/admin/users", "/machines" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        // The same word under the prefix is the API's, and behind the door.
        using var api = await client.GetAsync("/api/pages", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
    }
}
