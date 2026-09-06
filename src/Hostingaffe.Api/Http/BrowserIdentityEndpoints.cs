using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Hostingaffe.Api.Http;

public sealed record PasswordSignInRequest(string? Email, string? Password);
public sealed record BootstrapExchangeRequest(string? Token, string? Password);
public sealed record SecretPasswordRequest(string? Secret, string? Password);
public sealed record RecoveryRequest(string? Email);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? Password);

public static class BrowserIdentityEndpoints
{
    public static IEndpointRouteBuilder MapBrowserIdentity(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/session", async (PasswordSignInRequest? request, HttpContext http,
            SignInWithPassword signIn, LoginThrottle throttle, BrowserCookie cookie, CancellationToken ct) =>
        {
            string normalized;
            try { normalized = Domain.Identities.User.NormalizeEmailForComparison(request?.Email!); }
            catch (ArgumentException) { normalized = request?.Email?.Trim().ToLowerInvariant() ?? string.Empty; }
            var source = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (throttle.IsBlocked(normalized, source)) return Problems.Result(RefusalCode.Unauthenticated, "The email or password is not correct.");
            var issued = await signIn.ExecuteAsync(request?.Email, request?.Password, ct);
            if (issued is null) { throttle.Failed(normalized, source); return Problems.Result(RefusalCode.Unauthenticated, "The email or password is not correct."); }
            throttle.Succeeded(normalized); SetCookie(http, cookie, issued.Value); return Results.NoContent();
        }).AllowAnonymous().WithName("SignIn").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/session/bootstrap", async (BootstrapExchangeRequest? request, HttpContext http,
            ExchangeBootstrapToken exchange, BrowserCookie cookie, CancellationToken ct) =>
        {
            var issued = await exchange.ExecuteAsync(request?.Token, request?.Password, ct);
            if (issued is null) return Problems.Result(RefusalCode.Unauthenticated, "The bootstrap token cannot be exchanged.");
            SetCookie(http, cookie, issued.Value); return Results.NoContent();
        }).AllowAnonymous().WithName("ExchangeBootstrapToken").Produces(StatusCodes.Status204NoContent);

        endpoints.MapPost("/invitations/accept", async (SecretPasswordRequest? request, HttpContext http,
            AcceptInvitation accept, BrowserCookie cookie, CancellationToken ct) =>
        { var issued = await accept.ExecuteAsync(request?.Secret, request?.Password, ct); SetCookie(http, cookie, issued); return Results.NoContent(); })
            .AllowAnonymous().WithName("AcceptInvitation").Produces(StatusCodes.Status204NoContent);

        endpoints.MapPost("/password-recovery", async (RecoveryRequest? request, RequestPasswordRecovery recover, CancellationToken ct) =>
        { await recover.ExecuteAsync(request?.Email, ct); return Results.Accepted(); })
            .AllowAnonymous().WithName("RequestPasswordRecovery").Produces(StatusCodes.Status202Accepted);

        endpoints.MapPost("/password-recovery/complete", async (SecretPasswordRequest? request, CompletePasswordRecovery recover, CancellationToken ct) =>
        { await recover.ExecuteAsync(request?.Secret, request?.Password, ct); return Results.NoContent(); })
            .AllowAnonymous().WithName("CompletePasswordRecovery").Produces(StatusCodes.Status204NoContent);

        var door = endpoints.MapGroup(string.Empty).RequireAuthorization();
        door.MapDelete("/session", async (HttpContext http, ICallerIdentity caller, IBrowserSessions sessions,
            BrowserCookie cookie, TimeProvider clock, CancellationToken ct) =>
        { var who = caller.Caller.RequireUser("sign out"); if (who.SessionId is { } id) await sessions.RevokeAsync(id, who.Id, clock.GetUtcNow(), ct); http.Response.Cookies.Delete(cookie.Name, cookie.Options(DateTimeOffset.UnixEpoch)); return Results.NoContent(); })
            .WithName("SignOut").Produces(StatusCodes.Status204NoContent);
        door.MapGet("/sessions", (ListBrowserSessions list, CancellationToken ct) => list.ExecuteAsync(ct)).WithName("ListBrowserSessions");
        door.MapDelete("/sessions/{id:guid}", async (Guid id, ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock, CancellationToken ct) =>
        { var who = caller.Caller.RequireUser("revoke a browser session"); await sessions.RevokeAsync(id, who.Id, clock.GetUtcNow(), ct); return Results.NoContent(); })
            .WithName("RevokeBrowserSession").Produces(StatusCodes.Status204NoContent);
        door.MapDelete("/sessions", async (ICallerIdentity caller, IBrowserSessions sessions, TimeProvider clock, CancellationToken ct) =>
        { var who = caller.Caller.RequireUser("revoke browser sessions"); await sessions.RevokeAllAsync(who.Id, who.SessionId, clock.GetUtcNow(), ct); return Results.NoContent(); })
            .WithName("RevokeOtherBrowserSessions").Produces(StatusCodes.Status204NoContent);
        door.MapPost("/me/password", async (ChangePasswordRequest? request, ChangePassword change, CancellationToken ct) =>
        { await change.ExecuteAsync(request?.CurrentPassword, request?.Password, ct); return Results.NoContent(); })
            .WithName("ChangePassword").Produces(StatusCodes.Status204NoContent);
        return endpoints;
    }

    private static void SetCookie(HttpContext http, BrowserCookie cookie, (Domain.Identities.BrowserSession Session, string Secret) issued) =>
        http.Response.Cookies.Append(cookie.Name, issued.Secret, cookie.Options(issued.Session.ExpiresAt));
}

public sealed class BrowserCsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICallerIdentity caller, SmtpSettings smtp)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && context.User.Identity?.IsAuthenticated == true && caller.Caller.SessionId is not null
            && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            if (!CsrfProtection.IsSafe(context.Request, smtp.PublicUrl)) { await Problems.WriteAsync(context, RefusalCode.Csrf, "The browser write failed its CSRF check."); return; }
        }
        await next(context);
    }
}
