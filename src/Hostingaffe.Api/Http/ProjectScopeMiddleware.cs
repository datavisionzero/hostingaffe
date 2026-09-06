using Microsoft.AspNetCore.Routing.Patterns;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The single HTTP door for project content. Acts still constrain collection
/// queries; this guard makes every direct-key route indistinguishable from an
/// unknown key before its handler loads content.
/// </summary>
/// <remarks>
/// The route the endpoint was registered under is what is read here, never the
/// path the caller typed. Routing matches literal segments without regard to
/// case, so <c>/Projects/PLAN</c> reaches the same handler as
/// <c>/projects/PLAN</c>, and a guard that compared the request path let one of
/// the two through with no check at all. The pattern is ours and the route
/// values are the ones routing bound, which is why both are safe to switch on.
/// </remarks>
public sealed class ProjectScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ProjectScope scope, IProjects projects)
    {
        if (http.User.Identity?.IsAuthenticated == true && ProjectId(http, projects) is { } projectId)
            await scope.RequireAsync(await projectId, http.RequestAborted);

        await next(http);
    }

    private static Task<Guid>? ProjectId(HttpContext http, IProjects projects)
    {
        // No endpoint is no project content: an unrouted path is the SPA's
        // fallback or a 404, and neither loads a row.
        if (http.GetEndpoint() is not RouteEndpoint endpoint) return null;

        var route = endpoint.RoutePattern;
        var values = http.Request.RouteValues;

        switch (Literal(route, 0))
        {
            // Project assignment administration and lifecycle are explicit
            // administrator surfaces, not project content.
            case "projects" when Literal(route, 2) is "users" or "restore"
                || http.Request.Method == "DELETE" && route.PathSegments.Count == 2:
                return null;

            case "projects" when values["key"] is string projectKey:
                return FromProjectKey(projects, projectKey, http.RequestAborted);

            default:
                return null;
        }
    }

    /// <summary>The literal at <paramref name="index"/>, or null where the segment is a parameter.</summary>
    private static string? Literal(RoutePattern route, int index) =>
        index < route.PathSegments.Count && route.PathSegments[index] is { IsSimple: true } segment
        && segment.Parts[0] is RoutePatternLiteralPart literal
            ? literal.Content
            : null;

    private static async Task<Guid> FromProjectKey(IProjects projects, string key, CancellationToken cancellationToken) =>
        (await projects.FindByKeyAsync(key.Trim().ToUpperInvariant(), cancellationToken))?.Id ?? Guid.Empty;
}
