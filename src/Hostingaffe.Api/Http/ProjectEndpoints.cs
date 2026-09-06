using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <param name="Key">Upper case, a letter first, two to ten characters; never changed afterwards.</param>
public sealed record CreateProjectRequest(string? Key, string? Name, bool? TriageRequired, bool? ReviewRequired);

/// <summary>
/// Only what is present changes; the key is not among them.
/// <c>instructions_page</c> is the slug of the page every agent is handed with
/// every ticket, and present as <c>null</c> it takes the designation away. This
/// type is the contract's; the act takes <see cref="ProjectChanges"/>, which
/// tells absent from null.
/// </summary>
public sealed record ChangeProjectRequest(string? Name, bool? TriageRequired, bool? ReviewRequired, string? InstructionsPage);

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
                var project = await create.ExecuteAsync(
                    request?.Key, request?.Name, request?.TriageRequired ?? false, request?.ReviewRequired ?? false, cancellationToken);
                return Results.Created($"/projects/{project.Key}", project);
            })
            .WithName("CreateProject")
            .WithSummary("Create a project with its key and the `kind` label group. Users only.")
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
            .WithSummary("Change the name, the switches or the instructions page. Users only; the key is immutable.")
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

        // The question at the centre of the product (VISION 10): the list, and the act.
        door.MapGet("/{key}/next", (string key, HttpRequest http, bool? ready, string? epic, string? repo, int? limit, Next next, CancellationToken cancellationToken) =>
                next.PreviewAsync(key, new NextRequest(ready, epic, [.. http.Query["label"].OfType<string>()], repo, limit, null), cancellationToken))
            .WithName("PreviewNext")
            .WithSummary("What the caller would be handed, in that order — the ready-for-agents list — and why the rest is not on it.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapPost("/{key}/next", (string key, NextRequest? request, Next next, CancellationToken cancellationToken) =>
                next.TakeAsync(key, request ?? new NextRequest(null, null, null, null, null, null), cancellationToken))
            .WithName("TakeNext")
            .WithSummary("Take the highest-ranked workable issue and claim it for the caller, in one transaction. 200 with `issue: null` when nothing is workable.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapGet("/{key}/needs-you", async (string key, string? cursor, int? limit, int? wait, HttpRequest request, HttpResponse response, NeedsYou needsYou, CancellationToken cancellationToken) =>
            {
                var answer = await needsYou.WaitAsync(key, cursor, limit, wait, request.Headers.IfNoneMatch, cancellationToken);
                response.Headers.ETag = answer.ETag;
                return answer.Page is null ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(answer.Page);
            })
            .WithName("ListNeedsYou")
            .WithSummary("What only a human can resolve: questions, review, unready under triage, then stuck blocker chains.")
            .AddOpenApiOperationTransformer(NeedsYouValidator)
            .Produces<NeedsYouPage>()
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    // Present, present-as-null and absent are three things in a PATCH, and only
    // the raw document tells them apart — the same reading `PATCH /issues/{key}`
    // does, for the same reason: `instructions_page` set to null takes the
    // designation away, and leaving it out leaves it alone.
    private static ProjectChanges Changes(JsonElement body)
    {
        if (body.ValueKind is not JsonValueKind.Object)
        {
            throw Domain.Refusal.Validation("body", "A change is an object.");
        }

        return new ProjectChanges(
            Text(body, "name"),
            Flag(body, "triage_required"),
            Flag(body, "review_required"),
            body.TryGetProperty("instructions_page", out _),
            Text(body, "instructions_page"));
    }

    private static string? Text(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static bool? Flag(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static Task NeedsYouValidator(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-None-Match",
            In = ParameterLocation.Header,
            Description = "The ETag of the last page; with wait, return when that page changes.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });

        foreach (var status in new[] { "200", "304" })
        {
            var response = (OpenApiResponse)operation.Responses![status];
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["ETag"] = new OpenApiHeader
            {
                Description = "Validator for this needs-you page.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }

        return Task.CompletedTask;
    }
}
