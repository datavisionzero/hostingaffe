using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Machines (<c>docs/api.md</c>): the computers the instance is a record of,
/// reached by the key an operator chose for them (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// Retiring and deleting are two different ends. <c>status=retired</c> is the
/// normal one and keeps everything, and the machine only leaves the default
/// list; <c>DELETE</c> is for mistakes, takes the installations, files and
/// deployments with it, and is undone for the grace period (ADR 0013).
/// </remarks>
public static class MachineEndpoints
{
    public static IEndpointRouteBuilder MapMachines(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/machines")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string? status, string? kind, bool? retired, ListMachines list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(status, kind, retired ?? false, cancellationToken))
            .WithName("ListMachines")
            .WithSummary("Every machine as a slim MachineSummary, by key, without the descriptions. `status` and `kind` filter. Retired machines are left out unless `retired=true` or `status=retired`; not paginated.");

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

        door.MapDelete("/{key}", async (string key, MoveMachine move, CancellationToken cancellationToken) =>
            {
                await move.DeleteAsync(key, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteMachine")
            .WithSummary("Soft-delete a machine and everything on it — its installations, their files and deployments, and the vms it hosts. Its pages stay, still naming it. Deleting is for mistakes; retiring is `status=retired`.")
            .Produces(StatusCodes.Status204NoContent);

        door.MapPost("/{key}/restore", (string key, MoveMachine move, CancellationToken cancellationToken) =>
                move.RestoreAsync(key, cancellationToken))
            .WithName("RestoreMachine")
            .WithSummary("Bring a deleted machine back, with exactly what its deletion took — not with what was deleted on its own before that.")
            .Produces<MachineShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
