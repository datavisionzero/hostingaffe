using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>What <c>ha login</c> polls with.</summary>
public sealed record RedeemDeviceLoginRequest(string? DeviceCode);

/// <summary>What a person types on the confirmation screen.</summary>
public sealed record DeviceLoginDecisionRequest(string? UserCode);

/// <summary>
/// The device login of ADR 0005 — the only sign-in that works in an SSH
/// session, a CI job, a container and an agent's sandbox, because it is the
/// only one that needs no browser on the machine doing the asking.
/// </summary>
/// <remarks>
/// Two of the four endpoints are unauthenticated by definition: the flow exists
/// to turn no credential into one. What stands in for authentication is that a
/// signed-in user on some other machine has to approve within ten minutes, and
/// that the token which comes out is that user's own.
/// </remarks>
public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/device/logins", (BeginDeviceLogin begin, CancellationToken cancellationToken) =>
                begin.ExecuteAsync(cancellationToken))
            .AllowAnonymous()
            .WithName("BeginDeviceLogin")
            .WithSummary("Begin a login: a short code for the person, a long one for the client.")
            .Produces<BegunDeviceLogin>();

        // Every state but "approved" is its own refusal code, so that a client
        // keeps polling on exactly one of them and stops on the rest
        // (docs/api.md, The device login).
        endpoints.MapPost("/device/tokens", async (
                RedeemDeviceLoginRequest? request,
                RedeemDeviceLogin redeem,
                CancellationToken cancellationToken) =>
                await redeem.ExecuteAsync(request?.DeviceCode, cancellationToken))
            .AllowAnonymous()
            .WithName("RedeemDeviceLogin")
            .WithSummary("Poll: the user token once somebody has approved, a code that says why not until then.")
            .Produces<RedeemedDeviceLogin>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var door = endpoints.MapGroup("/device")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        door.MapGet("/logins/{code}", (string code, ReadDeviceLogin read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(code, cancellationToken))
            .WithName("ReadDeviceLogin")
            .WithSummary("What is waiting behind a code, for the screen that is about to approve it.")
            .Produces<WaitingDeviceLogin>();

        door.MapPost("/approvals", (
                DeviceLoginDecisionRequest? request, DecideDeviceLogin decide, CancellationToken cancellationToken) =>
                decide.ApproveAsync(request?.UserCode, cancellationToken))
            .WithName("ApproveDeviceLogin")
            .WithSummary("Approve a waiting login: the machine that began it collects the caller's own token.")
            .Produces<WaitingDeviceLogin>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        door.MapPost("/refusals", (
                DeviceLoginDecisionRequest? request, DecideDeviceLogin decide, CancellationToken cancellationToken) =>
                decide.DenyAsync(request?.UserCode, cancellationToken))
            .WithName("DenyDeviceLogin")
            .WithSummary("Refuse a waiting login: nobody at this terminal started it.")
            .Produces<WaitingDeviceLogin>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
