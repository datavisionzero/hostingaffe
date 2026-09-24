using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>Keyed sources of external hosting (ADR 0020).</summary>
public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviders(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/providers")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (ListProviders list, CancellationToken ct) => list.ExecuteAsync(ct))
            .WithName("ListProviders")
            .WithSummary("Every live provider, by key, without descriptions.");

        door.MapPost(string.Empty,
                async (CreateProviderRequest? request, string? note, CreateProvider create, CancellationToken ct) =>
                {
                    var provider = await create.ExecuteAsync(
                        request ?? new CreateProviderRequest(null, null, null), note, ct);
                    return Results.Created($"{Routes.Api}/providers/{provider.Key}", provider);
                })
            .WithName("CreateProvider")
            .WithSummary("Create a keyed provider with a name and Markdown description. `note` is written to history.")
            .Produces<ProviderShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/{key}", (string key, ReadProvider read, CancellationToken ct) =>
                read.ExecuteAsync(key, ct))
            .WithName("ReadProvider")
            .WithSummary("The provider's name, description, authors and timestamps. Use `GET /api/machines?provider=KEY` for its machines, including VMs.");

        door.MapGet("/{key}/history", (string key, ReadProviderHistory read, CancellationToken ct) =>
                read.ExecuteAsync(key, ct))
            .WithName("ReadProviderHistory")
            .WithSummary("The provider's changes, oldest first.");

        door.MapPatch("/{key}",
                (string key, ChangeProviderRequest? request, string? note, HttpRequest http,
                 ChangeProvider change, CancellationToken ct) =>
                    change.ExecuteAsync(key, request ?? new ChangeProviderRequest(null, null),
                        http.Headers.IfMatch.ToString(), note, ct))
            .WithName("ChangeProvider")
            .WithSummary("Change a provider's name, description or emblem, guarded by `If-Match` and recorded in history.")
            .Produces<ProviderShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapDelete("/{key}",
                async (string key, string? note, MoveProvider move, CancellationToken ct) =>
                {
                    await move.DeleteAsync(key, note, ct);
                    return Results.NoContent();
                })
            .WithName("DeleteProvider")
            .WithSummary("Soft-delete a provider only when no machine, including a deleted one, refers to it.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapPost("/{key}/restore", (string key, string? note, MoveProvider move, CancellationToken ct) =>
                move.RestoreAsync(key, note, ct))
            .WithName("RestoreProvider")
            .WithSummary("Restore a deleted provider under its reserved key.")
            .Produces<ProviderShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
