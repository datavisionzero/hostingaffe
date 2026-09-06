using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Deployments (<c>docs/api.md</c>): what actually ran on an installation, and
/// when it went live.
/// </summary>
/// <remarks>
/// <para>
/// They live under their installation, because that is what gives the number an
/// address: a deployment has no key, the instance numbers it per installation,
/// and the number is what the API and later the CLI address it by.
/// </para>
/// <para>
/// A deployment is not a history entry (VISION 7). It has a table and an
/// endpoint of its own, because the deployments are what an operator wants to
/// <em>read</em>, while the history is what they consult when something looks
/// wrong. The history under a deployment is the corrections made to it.
/// </para>
/// </remarks>
public static class DeploymentEndpoints
{
    public static IEndpointRouteBuilder MapDeployments(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/installations/{key}/deployments")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string key, ListDeployments list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(key, cancellationToken))
            .WithName("ListDeployments")
            .WithSummary("Every deployment of the installation, newest by `at` first — the order everything derived uses, so the list and the version agree.");

        door.MapPost(string.Empty, async (string key, RecordDeploymentRequest? request, RecordDeployment record, CancellationToken cancellationToken) =>
            {
                var deployment = await record.ExecuteAsync(
                    key, request ?? new RecordDeploymentRequest(null, null, null, null, null), cancellationToken);

                return Results.Created(
                    $"{Routes.Api}/installations/{key}/deployments/{deployment.Number}", deployment);
            })
            .WithName("RecordDeployment")
            .WithSummary("Record a deployment: only `version` is required. `at` defaults to now and may be set, so that history can be backfilled — the latest by `at` is what the installation runs.")
            .Produces<DeploymentShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/{number:int}", (string key, int number, ReadDeployment read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, number, cancellationToken))
            .WithName("ReadDeployment")
            .WithSummary("The complete deployment, with `previous` and `files` derived by `at`: the version before it, and the file revisions that were current when it went live.");

        door.MapPatch("/{number:int}", (string key, int number, CorrectDeploymentRequest? request, HttpRequest http, CorrectDeployment correct, CancellationToken cancellationToken) =>
                correct.ExecuteAsync(
                    key,
                    number,
                    request ?? new CorrectDeploymentRequest(null, null, null, null),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName("CorrectDeployment")
            .WithSummary("Correct `ref`, `at`, `ticket` or `note`, and the history says so. `version` and the installation are what the record is: a deployment with the wrong version is deleted and recorded again.")
            .Produces<DeploymentShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapGet("/{number:int}/history", (string key, int number, ReadDeploymentHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, number, cancellationToken))
            .WithName("ReadDeploymentHistory")
            .WithSummary("Every correction made to the deployment, oldest first. The deployments themselves are not history entries; this is what was changed about one.");

        door.MapDelete("/{number:int}", async (string key, int number, MoveDeployment move, CancellationToken cancellationToken) =>
            {
                await move.DeleteAsync(key, number, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteDeployment")
            .WithSummary("Soft-delete a deployment. This is the other half of the correction rule: one recorded with the wrong version is deleted and recorded again. Its number is not handed out a second time.")
            .Produces(StatusCodes.Status204NoContent);

        door.MapPost("/{number:int}/restore", (string key, int number, MoveDeployment move, CancellationToken cancellationToken) =>
                move.RestoreAsync(key, number, cancellationToken))
            .WithName("RestoreDeployment")
            .WithSummary("Bring a deleted deployment back, under the number it kept.")
            .Produces<DeploymentShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
