using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The one door: <c>Authorization: Bearer &lt;token&gt;</c> on everything but
/// <c>GET /version</c>, and <see cref="AuthenticateToken"/> deciding whom it
/// admits (<c>docs/api.md</c>, Conventions).
/// </summary>
/// <remarks>
/// A successful authentication leaves the <see cref="Caller"/> on the request,
/// where <see cref="CallerIdentity"/> answers the port the acts ask for. The
/// principal carries only the id — nothing downstream reads claims, because
/// the caller is a value the acts take whole, not a bag of strings to parse
/// back.
/// </remarks>
public static class TokenAuthentication
{
    public const string Scheme = "Bearer";

    public static IServiceCollection AddHostingaffeTokenAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(Scheme, null);

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICallerIdentity, CallerIdentity>();

        return services;
    }
}

/// <inheritdoc cref="TokenAuthentication"/>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BrowserCookie cookie,
    IBrowserSessions sessions,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers.Authorization.ToString();
        Caller? caller;
        if (!string.IsNullOrEmpty(presented))
        {
            caller = await Context.RequestServices
                .GetRequiredService<AuthenticateToken>()
                .ExecuteAsync(presented, Context.RequestAborted);
        }
        else if (Request.Cookies.TryGetValue(cookie.Name, out var sessionSecret))
        {
            try
            {
                var admitted = await sessions.AuthenticateAsync(BrowserSession.Hash(sessionSecret), clock.GetUtcNow(), Context.RequestAborted);
                caller = admitted is null ? null : Caller.Of(admitted.User, admitted.Session);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException) { caller = null; }
        }
        else return AuthenticateResult.NoResult();

        if (caller is null)
        {
            // Revoked, never issued, or not a bearer token at all. Which of them
            // it was is not said and is not knowable from the answer.
            return AuthenticateResult.Fail("The presented token admits nobody.");
        }

        Context.Features.Set(caller);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, caller.Id.ToString())], Scheme.Name));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    // The problem document rather than a bare status: an agent's loop branches
    // on the code, and 401 without a body would be the one refusal that has none.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(
            Context,
            RefusalCode.Unauthenticated,
            "Sign in with a browser session or send a user token or agent token as `Authorization: Bearer <token>`.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(Context, RefusalCode.Forbidden, detail: null);
}

/// <summary>
/// The port answered from the request: whoever the handler above admitted.
/// </summary>
public sealed class CallerIdentity(IHttpContextAccessor accessor) : ICallerIdentity
{
    public Caller Caller =>
        accessor.HttpContext?.Features.Get<Caller>()
        ?? throw new InvalidOperationException(
            "No authenticated caller on this request. Every endpoint but GET /version is behind the door.");
}
