using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Api.Http;

/// <param name="Name">Unique across users and agents, regardless of case; at most 100 characters.</param>
/// <param name="Administrator">Whether the new user administers the instance. Off when left out.</param>
public sealed record CreateUserRequest(string? Name, string? Email, bool? Administrator);
public sealed record ChangeAdministratorRequest(bool? Administrator);
public sealed record RenameUserRequest(string? Name);
public sealed record EmailChangeRequest(string? Email);
public sealed record SecretRequest(string? Secret);

/// <param name="Name">Assigned — two words and a number — when left out.</param>
public sealed record CreateAgentRequest(string? Name);

public sealed record RenameAgentRequest(string? Name);

public sealed class AgentMetadataRequest
{
    private string? _kind;
    private string? _harness;
    private string? _environment;
    private string? _version;

    public string? Kind
    {
        get => _kind;
        init { _kind = value; KindGiven = true; }
    }
    public string? Harness
    {
        get => _harness;
        init { _harness = value; HarnessGiven = true; }
    }
    public string? Environment
    {
        get => _environment;
        init { _environment = value; EnvironmentGiven = true; }
    }
    public string? Version
    {
        get => _version;
        init { _version = value; VersionGiven = true; }
    }

    [JsonIgnore] public bool KindGiven { get; private init; }
    [JsonIgnore] public bool HarnessGiven { get; private init; }
    [JsonIgnore] public bool EnvironmentGiven { get; private init; }
    [JsonIgnore] public bool VersionGiven { get; private init; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}

/// <summary>
/// Users, agents and tokens (<c>docs/api.md</c>): the human side of the
/// permission line. An agent may call none of these and is told
/// <c>forbidden</c> by the act, not by an absent route — the door is the same,
/// the answer says why.
/// </summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentities(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup(string.Empty)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        door.MapPost("/users", async (CreateUserRequest? request, CreateUser create, CancellationToken cancellationToken) =>
            {
                var created = await create.ExecuteAsync(request?.Name, request?.Email, request?.Administrator ?? false, cancellationToken);
                return Results.Created($"{Routes.Api}/users/{created.Id}", created);
            })
            .WithName("CreateUser")
            .WithSummary("Invite a user by email. Administrators only.")
            .Produces<UserSummary>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/users", (ListUsers list, CancellationToken cancellationToken) => list.ExecuteAsync(cancellationToken))
            .WithName("ListUsers")
            .WithSummary("Every user. Not paginated: the list is people.");

        door.MapPost("/users/{id}/invitation", async (string id, ResendInvitation resend, CancellationToken ct) =>
            { await resend.ExecuteAsync(id, ct); return Results.Accepted(); })
            .WithName("ResendInvitation").WithSummary("Replace and resend an invited user's invitation, by name or id. Administrators only.")
            .Produces(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status404NotFound);

        door.MapPost("/users/{id}/deactivate", (string id, ChangeUserLifecycle change, CancellationToken ct) =>
                change.ExecuteAsync(id, UserLifecycleChange.Deactivate, ct))
            .WithName("DeactivateUser").WithSummary("Deactivate a user, by name or id, and suspend every way they authenticate. Administrators only.")
            .Produces<UserSummary>().ProducesProblem(StatusCodes.Status409Conflict);
        door.MapPost("/users/{id}/reactivate", (string id, ChangeUserLifecycle change, CancellationToken ct) =>
                change.ExecuteAsync(id, UserLifecycleChange.Reactivate, ct))
            .WithName("ReactivateUser").WithSummary("Reactivate a deactivated user, by name or id. Administrators only.")
            .Produces<UserSummary>();
        door.MapPatch("/users/{id}", (string id, ChangeAdministratorRequest? request,
                ChangeUserLifecycle change, CancellationToken ct) =>
            {
                if (request?.Administrator is null) throw Refusal.Validation("administrator", "Administrator is required.");
                return change.ExecuteAsync(id, request.Administrator.Value
                    ? UserLifecycleChange.GrantAdministrator : UserLifecycleChange.RevokeAdministrator, ct);
            })
            .WithName("ChangeUserAdministrator").WithSummary("Grant or revoke the administrator role, by name or id. Administrators only.")
            .Produces<UserSummary>().ProducesProblem(StatusCodes.Status409Conflict);

        door.MapPost("/me/email", async (EmailChangeRequest? request, RequestEmailChange change, CancellationToken ct) =>
            { await change.ExecuteAsync(request?.Email, ct); return Results.Accepted(); })
            .WithName("RequestEmailChange").WithSummary("Send confirmation to a user's new email address.")
            .Produces(StatusCodes.Status202Accepted);

        door.MapPatch("/me", (RenameUserRequest? request, RenameUser rename, CancellationToken ct) =>
                rename.ExecuteAsync(request?.Name, ct))
            .WithName("RenameUser").WithSummary("Change the caller user's name.")
            .Produces<UserSummary>().ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapPost("/email-changes/confirm", async (SecretRequest? request, ConfirmEmailChange confirm, CancellationToken ct) =>
            { await confirm.ExecuteAsync(request?.Secret, ct); return Results.NoContent(); })
            .AllowAnonymous().WithName("ConfirmEmailChange").WithSummary("Confirm and apply a pending email change.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status410Gone);

        door.MapPost("/agents", async (CreateAgentRequest? request, CreateAgent create, CancellationToken cancellationToken) =>
            {
                var created = await create.ExecuteAsync(request?.Name, cancellationToken);
                return Results.Created($"{Routes.Api}/agents/{created.Id}", created);
            })
            .WithName("CreateAgent")
            .WithSummary("Create an agent and its one token, shown once. Users only.")
            .Produces<CreatedAgent>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/agents", (ListAgents list, CancellationToken cancellationToken) => list.ExecuteAsync(cancellationToken))
            .WithName("ListAgents")
            .WithSummary("Every agent with its owner and its token, revoked ones included.");

        door.MapPatch("/agents/{id}", (string id, RenameAgentRequest? request, RenameAgent rename, CancellationToken cancellationToken) =>
                rename.ExecuteAsync(id, request?.Name, cancellationToken))
            .WithName("RenameAgent")
            .WithSummary("Rename an agent, by name or id. Its owner or an administrator.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapDelete("/agents/{id}", async (string id, RevokeAgent revoke, CancellationToken cancellationToken) =>
            {
                await revoke.ExecuteAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RevokeAgent")
            .WithSummary("Revoke an agent's token, by name or id. The identity stays. Its owner or an administrator.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapPatch("/me/metadata", (AgentMetadataRequest? request, ReportAgentMetadata report, CancellationToken cancellationToken) =>
            {
                request ??= new AgentMetadataRequest();
                if (request.UnknownFields?.Keys.FirstOrDefault() is { } field)
                {
                    throw new Refusal(RefusalCode.UnknownField, $"Agent metadata has no field {field}.",
                        new Dictionary<string, object?> { ["field"] = field });
                }
                return report.ExecuteAsync(new AgentMetadataChanges(
                    request.KindGiven, request.Kind,
                    request.HarnessGiven, request.Harness,
                    request.EnvironmentGiven, request.Environment,
                    request.VersionGiven, request.Version), cancellationToken);
            })
            .WithName("ReportAgentMetadata")
            .WithSummary("Report the caller agent's stable metadata. An omitted field stays; null clears it.")
            .Produces<Me>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapGet("/tokens", (ListTokens list, CancellationToken cancellationToken) => list.ExecuteAsync(cancellationToken))
            .WithName("ListTokens")
            .WithSummary("The caller's own user tokens, revoked ones included.");

        door.MapPost("/tokens", async (CreateToken create, CancellationToken cancellationToken) =>
            {
                var issued = await create.ExecuteAsync(cancellationToken);
                return Results.Created($"{Routes.Api}/tokens/{issued.Id}", issued);
            })
            .WithName("CreateToken")
            .WithSummary("A further user token for the caller, shown once.")
            .Produces<IssuedToken>(StatusCodes.Status201Created);

        door.MapDelete("/tokens/{id:guid}", async (Guid id, RevokeToken revoke, CancellationToken cancellationToken) =>
            {
                await revoke.ExecuteAsync(id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RevokeToken")
            .WithSummary("Revoke one of the caller's own tokens.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
