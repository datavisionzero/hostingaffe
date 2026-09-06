using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Installations (<c>docs/api.md</c>): one software installed once on one
/// machine, reached by the key an operator chose (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// The list takes a filter per closed set and one per key, because the question
/// VISION 7 names — "every production installation without a backup" — has to be
/// one call. There is no delete here yet; it arrives for every entity at once.
/// </remarks>
public static class InstallationEndpoints
{
    public static IEndpointRouteBuilder MapInstallations(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/installations")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (
                string? machine,
                string? software,
                string? environment,
                string? role,
                string? status,
                string? backup,
                string? monitoring,
                string? logging,
                ListInstallations list,
                CancellationToken cancellationToken) =>
                list.ExecuteAsync(machine, software, environment, role, status, backup, monitoring, logging, cancellationToken))
            .WithName("ListInstallations")
            .WithSummary("Every installation as a slim InstallationSummary, by key, without the descriptions. Every parameter filters by one value; `environment=production&backup=none` is the question VISION 7 names. Not paginated.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapPost(string.Empty, async (CreateInstallationRequest? request, CreateInstallation create, CancellationToken cancellationToken) =>
            {
                var installation = await create.ExecuteAsync(
                    request ?? new CreateInstallationRequest(
                        null, null, null, null, null, null, null, null,
                        null, null, null, null, null, null, null, null),
                    cancellationToken);

                return Results.Created($"{Routes.Api}/installations/{installation.Key}", installation);
            })
            .WithName("CreateInstallation")
            .WithSummary("Create an installation: `key`, `machine`, `software`, `environment` and `role` are required, everything else may arrive later. `version` records the first deployment in the same transaction; without it there is no deployment yet, which is what a planned installation is.")
            .Produces<InstallationShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/{key}", (string key, ReadInstallation read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadInstallation")
            .WithSummary("The complete installation: every field, the machine and the software it names, and who touched it last.");

        door.MapGet("/{key}/history", (string key, ReadInstallationHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadInstallationHistory")
            .WithSummary("Every change to the installation, oldest first: who, when, which field, from what to what. A list reads as its entries; the description records that it changed, not how.");

        door.MapPatch("/{key}", (string key, ChangeInstallationRequest? request, HttpRequest http, ChangeInstallation change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    key,
                    request ?? new ChangeInstallationRequest(
                        null, null, null, null, null, null, null,
                        null, null, null, null, null, null, null),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName("ChangeInstallation")
            .WithSummary("Change any field but the key, which is immutable. A field left out stays as it is; the empty string clears a text field and an empty list clears a list. `If-Match` with the `updated_at` last read guards the write.")
            .Produces<InstallationShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        return endpoints;
    }
}
