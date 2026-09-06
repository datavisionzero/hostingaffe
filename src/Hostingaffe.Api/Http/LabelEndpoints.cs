using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

public sealed record CreateLabelRequest(string? Name, string? Group, string? Description);

/// <summary>
/// A <c>PATCH</c> body where <c>null</c> and absent mean different things:
/// <c>"group": null</c> takes the label out of its group, an absent group leaves
/// it. The converter below is what tells the two apart.
/// </summary>
[JsonConverter(typeof(ChangeLabelRequestConverter))]
public sealed record ChangeLabelRequest(string? Name, bool GroupGiven, string? Group, bool DescriptionGiven, string? Description);

/// <inheritdoc cref="ChangeLabelRequest"/>
public sealed class ChangeLabelRequestConverter : JsonConverter<ChangeLabelRequest>
{
    public override ChangeLabelRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var body = JsonElement.ParseValue(ref reader);
        if (body.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("A label change is an object.");
        }

        return new ChangeLabelRequest(
            Text(body, "name"),
            body.TryGetProperty("group", out _),
            Text(body, "group"),
            body.TryGetProperty("description", out _),
            Text(body, "description"));
    }

    public override void Write(Utf8JsonWriter writer, ChangeLabelRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Name is not null)
        {
            writer.WriteString("name", value.Name);
        }

        if (value.GroupGiven)
        {
            writer.WriteString("group", value.Group);
        }

        if (value.DescriptionGiven)
        {
            writer.WriteString("description", value.Description);
        }

        writer.WriteEndObject();
    }

    private static string? Text(JsonElement body, string property) =>
        body.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Labels (<c>docs/api.md</c>): the one extensibility the product offers, and
/// open to agents — an agent that breaks an assignment down needs to be able to
/// say what its tickets are.
/// </summary>
public static class LabelEndpoints
{
    public static IEndpointRouteBuilder MapLabels(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/projects/{key}/labels")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet(string.Empty, (string key, ListLabels list, CancellationToken cancellationToken) =>
                list.ExecuteAsync(key, cancellationToken))
            .WithName("ListLabels")
            .WithSummary("Every label of the project with its group and description. Not paginated.");

        door.MapPost(string.Empty, async (string key, CreateLabelRequest? request, CreateLabel create, CancellationToken cancellationToken) =>
            {
                var label = await create.ExecuteAsync(key, request?.Name, request?.Group, request?.Description, cancellationToken);
                return Results.Created($"/projects/{key}/labels/{Uri.EscapeDataString(label.Name)}", label);
            })
            .WithName("CreateLabel")
            .WithSummary("Create a label, optionally in a group, optionally with one line saying what it means.")
            .Produces<LabelShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // A label name may carry a slash (`repo/hostingaffe`), which reaches the
        // route encoded and is decoded here rather than by routing.
        door.MapPatch("/{name}", (string key, string name, ChangeLabelRequest? request, ChangeLabel change, CancellationToken cancellationToken) =>
                change.ExecuteAsync(
                    key,
                    Uri.UnescapeDataString(name),
                    new LabelChanges(request?.Name, request?.GroupGiven ?? false, request?.Group, request?.DescriptionGiven ?? false, request?.Description),
                    cancellationToken))
            .WithName("ChangeLabel")
            .WithSummary("Rename, regroup or describe a label. A group change that would leave an issue or an epic with two of one group is refused, and `issues` and `epics` say which.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapDelete("/{name}", async (string key, string name, DeleteLabel delete, CancellationToken cancellationToken) =>
            {
                await delete.ExecuteAsync(key, Uri.UnescapeDataString(name), cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteLabel")
            .WithSummary("Soft-delete a label; it vanishes from every issue until restored.")
            .Produces(StatusCodes.Status204NoContent);

        door.MapPost("/{name}/restore", (string key, string name, RestoreLabel restore, CancellationToken cancellationToken) =>
                restore.ExecuteAsync(key, Uri.UnescapeDataString(name), cancellationToken))
            .WithName("RestoreLabel")
            .WithSummary("Bring a deleted label back, with its attachments.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
