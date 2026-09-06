using Hostingaffe.Api.Hosting;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>What <c>GET /version</c> answers.</summary>
public sealed record VersionResponse(string Version);

/// <summary>
/// The instance and the caller (<c>docs/api.md</c>, Endpoints): the one
/// endpoint outside the door, and the first one a client calls inside it.
/// </summary>
public static class InstanceEndpoints
{
    public static IEndpointRouteBuilder MapInstance(this IEndpointRouteBuilder endpoints)
    {
        // No authentication, by design: the CLI asks this before it knows
        // whether its token is any good, to report skew as skew and not as a
        // refusal.
        endpoints.MapGet("/version", () => new VersionResponse(InstanceVersion.Value))
            .AllowAnonymous()
            .WithName("ReadVersion")
            .WithSummary("The version of this instance.");

        endpoints.MapGet("/me", (ReadMe readMe, CancellationToken cancellationToken) =>
                readMe.ExecuteAsync(cancellationToken))
            .RequireAuthorization()
            .WithName("ReadMe")
            .WithSummary("The caller: identity, role, owner and the token it came in under.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
