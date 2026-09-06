using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// A <c>PATCH</c> body where <c>null</c> and absent mean different things:
/// <c>"body": null</c> empties the document, an absent body leaves it. The
/// converter below is what tells the two apart.
/// </summary>
[JsonConverter(typeof(ChangePageRequestConverter))]
public sealed record ChangePageRequest(string? Slug, string? Title, bool BodyGiven, string? Body);

/// <inheritdoc cref="ChangePageRequest"/>
public sealed class ChangePageRequestConverter : JsonConverter<ChangePageRequest>
{
    public override ChangePageRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var body = JsonElement.ParseValue(ref reader);
        if (body.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("A page change is an object.");
        }

        return new ChangePageRequest(
            Text(body, "slug"),
            Text(body, "title"),
            body.TryGetProperty("body", out _),
            Text(body, "body"));
    }

    public override void Write(Utf8JsonWriter writer, ChangePageRequest value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        if (value.Slug is not null)
        {
            writer.WriteString("slug", value.Slug);
        }

        if (value.Title is not null)
        {
            writer.WriteString("title", value.Title);
        }

        if (value.BodyGiven)
        {
            writer.WriteString("body", value.Body);
        }

        writer.WriteEndObject();
    }

    private static string? Text(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Pages (<c>docs/api.md</c>): the instance's flat wiki, reached by the slug
/// that is a page's address rather than by a key (ADR 0021).
/// </summary>
public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPages(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/pages")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string? q, ListPages list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(q, cancellationToken))
            .WithName("ListPages")
            .WithSummary("Every page as a slim PageSummary, by slug, without the bodies. `q` is the full-text filter over title and body; not paginated.");

        door.MapPost(string.Empty, async (CreatePageRequest? request, CreatePage create, CancellationToken cancellationToken) =>
            {
                var page = await create.ExecuteAsync(request ?? new CreatePageRequest(null, null, null), cancellationToken);
                return Results.Created($"{Routes.Api}/pages/{page.Slug}", page);
            })
            .WithName("CreatePage")
            .WithSummary("Create a page: the slug is given, never derived from the title.")
            .Produces<PageShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapGet("/{slug}", (string slug, ReadPage read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(slug, cancellationToken))
            .WithName("ReadPage")
            .WithSummary("The complete page: the Markdown, the author and who touched it last.");

        door.MapGet("/{slug}/history", (string slug, ReadPageHistory read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(slug, cancellationToken))
            .WithName("ReadPageHistory")
            .WithSummary("Every change to the page, oldest first: who, when, which field, from what to what. A text records that it changed, not how.");

        door.MapPatch("/{slug}", (string slug, ChangePageRequest? request, HttpRequest http, ChangePage change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    slug,
                    new PageChanges(request?.Slug, request?.Title, request?.BodyGiven ?? false, request?.Body),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName("ChangePage")
            .WithSummary("Change the title, the Markdown or the slug; `If-Match` with the `updated_at` last read guards the document. A rename leaves nothing behind at the old slug.")
            .Produces<PageShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapDelete("/{slug}", async (string slug, MovePage move, CancellationToken cancellationToken) =>
            {
                await move.DeleteAsync(slug, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeletePage")
            .WithSummary("Soft-delete a page; its slug stays spent until the purge, so a restore can never land on a taken name.")
            .Produces(StatusCodes.Status204NoContent);

        door.MapPost("/{slug}/restore", (string slug, MovePage move, CancellationToken cancellationToken) =>
                move.RestoreAsync(slug, cancellationToken))
            .WithName("RestorePage")
            .WithSummary("Bring a deleted page back, under the slug it kept.")
            .Produces<PageShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
