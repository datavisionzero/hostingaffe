using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// A <c>PATCH</c> body where <c>null</c> and absent mean different things:
/// <c>"body": null</c> empties the document, an absent body leaves it. The
/// converter below is what tells the two apart.
/// </summary>
[JsonConverter(typeof(ChangePageRequestConverter))]
public sealed record ChangePageRequest(string? Slug, string? Title, bool BodyGiven, string? Body, IReadOnlyList<string>? Labels);

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
            Text(body, "body"),
            body.TryGetProperty("labels", out var labels) && labels.ValueKind is JsonValueKind.Array
                ? [.. labels.EnumerateArray().Select(l => l.GetString() ?? string.Empty)]
                : null);
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

        if (value.Labels is not null)
        {
            writer.WritePropertyName("labels");
            JsonSerializer.Serialize(writer, value.Labels, options);
        }

        writer.WriteEndObject();
    }

    private static string? Text(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Pages (<c>docs/api.md</c>): the project's flat wiki, under the project the
/// way labels and releases are, because a page is named within a project rather
/// than carrying a key that already says which one (ADR 0021).
/// </summary>
public static class PageEndpoints
{
    public static IEndpointRouteBuilder MapPages(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/projects/{key}/pages")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string key, string? q, HttpRequest http, ListPages list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(key, [.. http.Query["label"].OfType<string>()], q, cancellationToken))
            .WithName("ListPages")
            .WithSummary("Every page of the project as a slim PageSummary, by slug, without the bodies. `q` is the full-text filter over title and body; not paginated, and `label` repeats.")
            .AddOpenApiOperationTransformer(RepeatableLabel);

        door.MapPost(string.Empty, async (string key, CreatePageRequest? request, CreatePage create, CancellationToken cancellationToken) =>
            {
                var page = await create.ExecuteAsync(key, request ?? new CreatePageRequest(null, null, null, null), cancellationToken);
                return Results.Created($"/projects/{key}/pages/{page.Slug}", page);
            })
            .WithName("CreatePage")
            .WithSummary("Create a page: the slug is given, never derived from the title.")
            .Produces<PageShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        door.MapGet("/{slug}", (string key, string slug, ReadPage read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, slug, cancellationToken))
            .WithName("ReadPage")
            .WithSummary("The complete page: the Markdown, the author, who touched it last, the labels.");

        door.MapPatch("/{slug}", (string key, string slug, ChangePageRequest? request, HttpRequest http, ChangePage change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    key,
                    slug,
                    new PageChanges(request?.Slug, request?.Title, request?.BodyGiven ?? false, request?.Body, request?.Labels),
                    http.Headers.IfMatch.ToString(),
                    cancellationToken))
            .WithName("ChangePage")
            .WithSummary("Change the title, the Markdown, the labels or the slug; `If-Match` with the `updated_at` last read guards the document. A rename leaves nothing behind at the old slug.")
            .Produces<PageShape>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        door.MapDelete("/{slug}", async (string key, string slug, MovePage move, CancellationToken cancellationToken) =>
            {
                await move.DeleteAsync(key, slug, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeletePage")
            .WithSummary("Soft-delete a page; its slug stays spent until the purge, so a restore can never land on a taken name.")
            .Produces(StatusCodes.Status204NoContent);

        door.MapPost("/{slug}/restore", (string key, string slug, MovePage move, CancellationToken cancellationToken) =>
                move.RestoreAsync(key, slug, cancellationToken))
            .WithName("RestorePage")
            .WithSummary("Bring a deleted page back, under the slug it kept.")
            .Produces<PageShape>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    /// <summary>
    /// <c>label</c> is read from the query rather than bound, so the contract
    /// is told about it here — otherwise neither generated client can send it
    /// and both would have to build the URL by hand.
    /// </summary>
    private static Task RepeatableLabel(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "label",
            In = ParameterLocation.Query,
            Description = "A label the page carries; repeat the parameter for several, all of which it must carry.",
            Style = ParameterStyle.Form,
            Explode = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        });

        return Task.CompletedTask;
    }
}
