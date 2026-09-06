using Hostingaffe.Application.Acts;
using Hostingaffe.Domain;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Files (<c>docs/api.md</c>): the text a machine runs with, addressed under
/// the machine or the installation it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// A file has no key. Its address is its owner and its path, and a path has
/// slashes in it, so the path is the last thing in the address — which is what
/// puts the revisions under a second word beside <c>files</c> rather than after
/// the path, where nothing could tell a sub-resource from a directory.
/// </para>
/// <para>
/// Both owners get the same six endpoints from the same code. A file of a
/// machine and a file of an installation are the same thing under a different
/// owner, and two copies of this would be one that drifts.
/// </para>
/// </remarks>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFiles(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFilesOf(AnchorKind.Machine, "machines");
        endpoints.MapFilesOf(AnchorKind.Installation, "installations");

        return endpoints;
    }

    private static void MapFilesOf(this IEndpointRouteBuilder endpoints, AnchorKind kind, string collection)
    {
        var owner = kind is AnchorKind.Machine ? "machine" : "installation";

        var door = endpoints.MapGroup($"/{collection}/{{key}}")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet("/files", (string key, ListFiles list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(kind, key, cancellationToken))
            .WithName($"List{kind}Files")
            .WithSummary($"Every file of the {owner}, by path, as a slim FileSummary — without the contents.");

        door.MapPost("/files", async (string key, CreateFileRequest? request, CreateFile create, CancellationToken cancellationToken) =>
            {
                var file = await create.ExecuteAsync(
                    kind, key, request ?? new CreateFileRequest(null, null, null), cancellationToken);

                return Results.Created($"{Routes.Api}/{collection}/{key}/files/{file.Path}", file);
            })
            .WithName($"Create{kind}File")
            .WithSummary($"Put a file under the {owner}: `path` is relative and unique under it, and the content is its first revision. The refused paths are `.env` and every `.env.*` but `.env.example`, anything under `secrets/`, and anything outside the owner's directory.")
            .Produces<FileShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/files/{**path}", (string key, string path, int? revision, ReadFile read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(kind, key, path, revision, cancellationToken))
            .WithName($"Read{kind}File")
            .WithSummary("The file with its content. `revision` reads it as it was at that write; without it, as it is now.");

        door.MapPut("/files/{**path}", (string key, string path, WriteFileRequest? request, HttpRequest http, WriteFile write, CancellationToken cancellationToken) =>
                write.ExecuteAsync(
                    kind,
                    key,
                    path,
                    request ?? new WriteFileRequest(null, null),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName($"Write{kind}File")
            .WithSummary("Write the file: a new revision, unless it already says exactly this. A field left out stays as it is. `If-Match` with the `updated_at` last read guards the write.")
            .Produces<FileShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapGet("/file-revisions/{**path}", (string key, string path, ReadFileRevisions read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(kind, key, path, cancellationToken))
            .WithName($"Read{kind}FileRevisions")
            .WithSummary("Every write of the file, newest first, without what each of them wrote. One of them is read by asking for the file with `revision`.");

        door.MapGet("/file-history/{**path}", (string key, string path, ReadFileHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(kind, key, path, cancellationToken))
            .WithName($"Read{kind}FileHistory")
            .WithSummary("Every change to the file, oldest first: who, when, and which revision it became. What each revision said is the revision's, not the history's.");
    }
}
