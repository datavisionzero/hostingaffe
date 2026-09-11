using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The bulk write (<c>docs/api.md</c>, Importing): a whole host — its software,
/// its installations, their files and their deployments — in one transaction,
/// because documenting a host is one act and not thirty calls.
/// </summary>
/// <remarks>
/// The document is the one <c>ha export</c> writes, so a migration out of an old
/// repository has a defined target (VISION 14). What arrives is the record and
/// not the history behind it (<c>docs/api.md</c>, Importing). It sits beside the
/// objects rather than under one, because it creates more kinds than any one of
/// them is.
/// </remarks>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImport(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/import", (ImportRequest? request, string? note, ImportRecord import, CancellationToken cancellationToken) =>
                import.ExecuteAsync(request ?? new ImportRequest(null, null, null), note, cancellationToken))
            .RequireAuthorization()
            .WithName("Import")
            .WithSummary(
                "Create a whole record from one document — machines with their installations, files and deployments, "
                + "the software they are of, and pages — in one transaction. All or nothing: a refusal anywhere leaves "
                + "nothing standing. The document is what `ha export` writes, so what only the instance writes "
                + "(`created_by`, `updated_at`, the history, a file's `revision`, a deployment's `number`) is read past "
                + "by name and anything else is `unknown-field`. A file arrives at the content it is at, as its first "
                + "revision. `note` goes into the history beside every change it makes (ADR 0004).")
            .Produces<ImportedShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
