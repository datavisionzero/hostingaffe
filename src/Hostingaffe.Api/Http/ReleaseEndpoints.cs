using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

public static class ReleaseEndpoints
{
    public static IEndpointRouteBuilder MapReleases(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/projects/{key}/releases").RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);
        door.MapGet(string.Empty, (string key, ListReleases list, CancellationToken ct) => list.ExecuteAsync(key, ct))
            .WithName("ListReleases").WithSummary("Every release of the project: the open one first, then published ones newest first.");
        door.MapGet("/{name}", (string key, string name, ReadRelease read, CancellationToken ct) => read.ExecuteAsync(key, name, ct))
            .WithName("ReadRelease").WithSummary("One release with the issues that shipped in it.").ProducesProblem(StatusCodes.Status404NotFound);
        door.MapPatch("/{name}", async (string key, string name, ChangeReleaseRequest? request, ChangeRelease change, CancellationToken ct) =>
            await change.ExecuteAsync(key, name, request ?? new(null, null), ct))
            .WithName("ChangeRelease").WithSummary("Annotate the release notes, and correct the name of the newest publication. A field left out is left alone.")
            .Accepts<ChangeReleaseRequest>("application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        door.MapPost("/publish", async (string key, PublishReleaseRequest? request, PublishRelease publish, CancellationToken ct) =>
            {
                var release = await publish.ExecuteAsync(key, request ?? new(null, null), ct);
                return Results.Created($"/projects/{key}/releases/{Uri.EscapeDataString(release.Name)}", release);
            })
            .WithName("PublishRelease").WithSummary("Name and freeze the open release, then create the next open one.")
            .Produces<ReleaseShape>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        door.MapPost("/{name}/retract", (string key, string name, RetractRelease retract, CancellationToken ct) => retract.ExecuteAsync(key, name, ct))
            .WithName("RetractRelease")
            .WithSummary("Take the newest publication back: the release is the open one again and the empty open release goes. Refused once another followed it.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        door.MapPut("/{name}/issues/{issue}", (string key, string name, string issue, ChangeReleaseIssues change, CancellationToken ct) => change.AddAsync(key, name, issue, ct))
            .WithName("AddIssueToRelease")
            .WithSummary("Put an issue into the open release by hand. A published release is a record and takes none.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        door.MapDelete("/{name}/issues/{issue}", (string key, string name, string issue, ChangeReleaseIssues change, CancellationToken ct) => change.RemoveAsync(key, name, issue, ct))
            .WithName("RemoveIssueFromRelease")
            .WithSummary("Take an issue out of the open release: it has not shipped yet and does not belong.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints;
    }
}
