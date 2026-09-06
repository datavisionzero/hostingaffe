using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

public sealed record SendTestEmailRequest(string? Email);

public static class SmtpEndpoints
{
    public static IEndpointRouteBuilder MapSmtp(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/smtp")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        admin.MapGet(string.Empty, (ReadSmtpStatus read) => read.Execute())
            .WithName("ReadSmtpStatus")
            .WithSummary("Whether transactional email is configured, without credentials.");

        admin.MapPost("/test", async (SendTestEmailRequest? request, SendTestEmail send, CancellationToken cancellationToken) =>
            {
                await send.ExecuteAsync(request?.Email, cancellationToken);
                return Results.Accepted();
            })
            .WithName("SendTestEmail")
            .WithSummary("Send a transactional-email test message. Administrators only.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }
}
