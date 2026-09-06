using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Machines (<c>docs/api.md</c>): the computers the instance is a record of,
/// reached by the key an operator chose for them (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// There is no delete here yet. Retiring and deleting are one decision with two
/// ends, and they arrive together for every entity rather than one endpoint at
/// a time.
/// </remarks>
public static class MachineEndpoints
{
    public static IEndpointRouteBuilder MapMachines(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/machines")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string? status, string? kind, ListMachines list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(status, kind, cancellationToken))
            .WithName("ListMachines")
            .WithSummary("Every machine as a slim MachineSummary, by key, without the descriptions. `status` and `kind` filter; not paginated.");

        door.MapPost(string.Empty, async (CreateMachineRequest? request, CreateMachine create, CancellationToken cancellationToken) =>
            {
                var machine = await create.ExecuteAsync(
                    request ?? new CreateMachineRequest(
                        null, null, null, null, null, null, null, null, null, null,
                        null, null, null, null, null, null, null, null, null, null),
                    cancellationToken);

                return Results.Created($"{Routes.Api}/machines/{machine.Key}", machine);
            })
            .WithName("CreateMachine")
            .WithSummary("Create a machine: the key and the kind are required, everything else may arrive later. `host` is a vm's only.")
            .Produces<MachineShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/{key}", (string key, ReadMachine read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadMachine")
            .WithSummary("The complete machine: every field, the host it runs on, and who touched it last.");

        door.MapGet("/{key}/history", (string key, ReadMachineHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadMachineHistory")
            .WithSummary("Every change to the machine, oldest first: who, when, which field, from what to what. The description records that it changed, not how.");

        door.MapPatch("/{key}", (string key, ChangeMachineRequest? request, HttpRequest http, ChangeMachine change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    key,
                    request ?? new ChangeMachineRequest(
                        null, null, null, null, null, null, null, null, null,
                        null, null, null, null, null, null, null, null, null, null),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName("ChangeMachine")
            .WithSummary("Change any field but the key, which is immutable. A field left out stays as it is; the empty string clears a text field. `If-Match` with the `updated_at` last read guards the write.")
            .Produces<MachineShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        return endpoints;
    }
}
