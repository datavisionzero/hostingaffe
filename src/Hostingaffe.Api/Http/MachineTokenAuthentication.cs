using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The second door, and the narrowest one: <c>Authorization: Bearer
/// &lt;machine token&gt;</c> on the one endpoint a machine reaches
/// (<c>docs/api.md</c>, Reports; ADR 0016).
/// </summary>
/// <remarks>
/// <para>
/// It is a scheme of its own rather than a branch inside the ordinary one,
/// because the two admit different things and must never fall through to each
/// other: a user token is not a machine, and a machine token is not a caller.
/// Whatever this admits is a <see cref="MachineCaller"/> on the request, and
/// <see cref="CallerMachine"/> is what the act reads it from.
/// </para>
/// <para>
/// Nothing here says whether a rejected secret was a valid token elsewhere. A
/// browser cookie is not looked at either — a report comes from a host with a
/// cron, not from a session.
/// </para>
/// </remarks>
public static class MachineTokenAuthentication
{
    public const string Scheme = "MachineBearer";

    /// <summary>The policy the one reporting endpoint requires.</summary>
    public const string Policy = "MachineToken";

    public static IServiceCollection AddHostingaffeMachineAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, MachineTokenAuthenticationHandler>(Scheme, null);

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser());

        services.AddScoped<ICallerMachine, CallerMachine>();

        return services;
    }
}

/// <inheritdoc cref="MachineTokenAuthentication"/>
public sealed class MachineTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(presented))
        {
            return AuthenticateResult.NoResult();
        }

        var machine = await Context.RequestServices
            .GetRequiredService<AuthenticateMachineToken>()
            .ExecuteAsync(presented, Context.RequestAborted);

        if (machine is null)
        {
            return AuthenticateResult.Fail("The presented token is not a machine's.");
        }

        Context.Features.Set(machine);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, machine.MachineId.ToString())], Scheme.Name));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(
            Context,
            RefusalCode.Unauthenticated,
            "A report is handed in under the machine's own token: `Authorization: Bearer <machine token>`. "
            + "A user token and an agent token are not one, and a machine token reaches nothing else.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problems.WriteAsync(Context, RefusalCode.Forbidden, detail: null);
}

/// <summary>The port answered from the request: whichever machine the handler above admitted.</summary>
public sealed class CallerMachine(IHttpContextAccessor accessor) : ICallerMachine
{
    public MachineCaller Machine =>
        accessor.HttpContext?.Features.Get<MachineCaller>()
        ?? throw new InvalidOperationException(
            "No authenticated machine on this request. Only the reporting endpoint is behind that door.");
}
