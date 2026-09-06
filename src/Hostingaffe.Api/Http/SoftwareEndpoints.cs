using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Software (<c>docs/api.md</c>): what the installations of this instance are
/// installations of, reached by the key an operator chose
/// (<c>CONTEXT.md</c>, Key).
/// </summary>
/// <remarks>
/// <para>
/// The collection is <c>/api/software</c> and not <c>/api/softwares</c>: the
/// word is uncountable in English, and the plural is circumscribed wherever it
/// is needed rather than invented here.
/// </para>
/// <para>
/// A software with installations is not deleted at all — not cascaded, refused,
/// with a problem document that says how many there are. That is the one place
/// in the record where a deletion is answered with a number instead of a
/// deletion.
/// </para>
/// </remarks>
public static class SoftwareEndpoints
{
    public static IEndpointRouteBuilder MapSoftware(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/software")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (ListSoftware list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(cancellationToken))
            .WithName("ListSoftware")
            .WithSummary("Every software as a slim SoftwareSummary, by key, without the descriptions. Not paginated.");

        door.MapPost(string.Empty, async (CreateSoftwareRequest? request, string? note, CreateSoftware create, CancellationToken cancellationToken) =>
            {
                var software = await create.ExecuteAsync(
                    request ?? new CreateSoftwareRequest(null, null, null, null, null, null),
                    note,
                    cancellationToken);

                return Results.Created($"{Routes.Api}/software/{software.Key}", software);
            })
            .WithName("CreateSoftware")
            .WithSummary("Create a software: the key is required, everything else may arrive later. `image` is a container image name without a tag. `note` goes into the history beside the change (ADR 0004).")
            .Produces<SoftwareShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/{key}", (string key, ReadSoftware read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadSoftware")
            .WithSummary("The complete software: every field, and who touched it last. It carries no version — versions belong to deployments.");

        door.MapGet("/{key}/history", (string key, ReadSoftwareHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadSoftwareHistory")
            .WithSummary("Every change to the software, oldest first: who, when, which field, from what to what. The description records that it changed, not how.");

        door.MapPatch("/{key}", (string key, ChangeSoftwareRequest? request, string? note, HttpRequest http, ChangeSoftware change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    key,
                    request ?? new ChangeSoftwareRequest(null, null, null, null, null),
                    http.Headers.IfMatch.ToString(),
                    note,
                    cancellationToken))
            .WithName("ChangeSoftware")
            .WithSummary("Change any field but the key, which is immutable. A field left out stays as it is; the empty string clears a text field. `If-Match` with the `updated_at` last read guards the write. `note` goes into the history beside the change (ADR 0004).")
            .Produces<SoftwareShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapDelete("/{key}", async (string key, string? note, MoveSoftware move, CancellationToken cancellationToken) =>
            {
                await move.DeleteAsync(key, note, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteSoftware")
            .WithSummary("Soft-delete a software. One with installations still on it is refused, and the problem document says how many; nothing cascades from here. `note` goes into the history beside the change (ADR 0004).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapPost("/{key}/restore", (string key, string? note, MoveSoftware move, CancellationToken cancellationToken) =>
                move.RestoreAsync(key, note, cancellationToken))
            .WithName("RestoreSoftware")
            .WithSummary("Bring a deleted software back, under the key it kept. `note` goes into the history beside the change (ADR 0004).")
            .Produces<SoftwareShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
