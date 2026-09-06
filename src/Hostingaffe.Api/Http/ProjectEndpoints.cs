using System.Text.Json;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <param name="Key">Upper case, a letter first, two to ten characters; never changed afterwards.</param>
public sealed record CreateProjectRequest(string? Key, string? Name);

/// <summary>
/// Only what is present changes; the key is not among them.
/// <c>instructions_page</c> is the slug of the page every agent is handed with
/// every ticket, and present as <c>null</c> it takes the designation away. This
/// type is the contract's; the act takes <see cref="ProjectChanges"/>, which
/// tells absent from null.
/// </summary>
public sealed record ChangeProjectRequest(string? Name, string? InstructionsPage);

/// <summary>Projects (<c>docs/api.md</c>): read by anyone, changed by a user, deleted by an administrator.</summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/projects", (string? deleted, ListAdminProjects list, CancellationToken ct) =>
                list.ExecuteAsync(deleted, ct))
            .RequireAuthorization().WithName("ListAdminProjects")
            .WithSummary("Every project, optionally including deleted projects. Administrators only.")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);

        var door = endpoints.MapGroup("/projects")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        door.MapPost(string.Empty, async (CreateProjectRequest? request, CreateProject create, CancellationToken cancellationToken) =>
            {
                var project = await create.ExecuteAsync(request?.Key, request?.Name, cancellationToken);
                return Results.Created($"/projects/{project.Key}", project);
            })
            .WithName("CreateProject")
            .WithSummary("Create a project with the key that prefixes everything in it. Users only.")
            .Produces<ProjectShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        door.MapGet(string.Empty, (ListProjects list, CancellationToken cancellationToken) => list.ExecuteAsync(cancellationToken))
            .WithName("ListProjects")
            .WithSummary("Every project the caller sees. Not paginated.");

        door.MapGet("/{key}", (string key, ReadProject read, CancellationToken cancellationToken) => read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadProject")
            .WithSummary("One project by key.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapPatch("/{key}", async (string key, HttpRequest http, ChangeProject change, CancellationToken cancellationToken) =>
            {
                var body = await JsonDocument.ParseAsync(http.Body, cancellationToken: cancellationToken);
                return await change.ExecuteAsync(key, Changes(body.RootElement), cancellationToken);
            })
            .WithName("ChangeProject")
            .WithSummary("Change the name or the instructions page. Users only; the key is immutable.")
            .Accepts<ChangeProjectRequest>("application/json")
            .Produces<ProjectShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapDelete("/{key}", async (string key, DeleteProject delete, CancellationToken cancellationToken) =>
            {
                await delete.ExecuteAsync(key, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteProject")
            .WithSummary("Soft-delete the project with everything in it. Administrators only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapPost("/{key}/restore", (string key, RestoreProject restore, CancellationToken cancellationToken) =>
                restore.ExecuteAsync(key, cancellationToken))
            .WithName("RestoreProject")
            .WithSummary("Bring a deleted project back, with everything in it. Administrators only.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapGet("/{key}/users", (string key, ListProjectUsers list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(key, cancellationToken))
            .WithName("ListProjectUsers")
            .WithSummary("Assigned users. Assigned users and administrators only.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapPut("/{key}/users/{id:guid}", async (string key, Guid id, GrantProjectAccess grant, CancellationToken cancellationToken) =>
            {
                await grant.ExecuteAsync(key, id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("GrantProjectAccess")
            .WithSummary("Grant project access to a user. Administrators only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapDelete("/{key}/users/{id:guid}", async (string key, Guid id, RevokeProjectAccess revoke, CancellationToken cancellationToken) =>
            {
                await revoke.ExecuteAsync(key, id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RevokeProjectAccess")
            .WithSummary("Remove a user's project access. Administrators only.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // Present, present-as-null and absent are three things in a PATCH, and only
    // the raw document tells them apart: `instructions_page` set to null takes
    // the designation away, and leaving it out leaves it alone.
    private static ProjectChanges Changes(JsonElement body)
    {
        if (body.ValueKind is not JsonValueKind.Object)
        {
            throw Domain.Refusal.Validation("body", "A change is an object.");
        }

        return new ProjectChanges(
            Text(body, "name"),
            body.TryGetProperty("instructions_page", out _),
            Text(body, "instructions_page"));
    }

    private static string? Text(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}
