using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Hostingaffe.Domain;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The one place a refusal becomes a problem document (<c>docs/api.md</c>,
/// Errors): every error is <c>application/problem+json</c> with a stable,
/// relative <c>type</c> whose last segment is the code a client switches on.
/// </summary>
/// <remarks>
/// The status of each code is here and nowhere else — not in Domain, where the
/// codes live, because a status is HTTP's word for it and the CLI has its own.
/// </remarks>
public static class Problems
{
    public const string ContentType = "application/problem+json";

    private const string TypePrefix = "/problems/";

    /// <summary>The wire spelling of a code: <c>ClaimHeld</c> is <c>claim-held</c>.</summary>
    public static string CodeOf(RefusalCode code) => JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString());

    public static string TypeOf(RefusalCode code) => TypePrefix + CodeOf(code);

    public static int StatusOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation or RefusalCode.UnknownField or RefusalCode.CursorInvalid => StatusCodes.Status400BadRequest,
        RefusalCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        RefusalCode.Csrf or RefusalCode.Forbidden or RefusalCode.ClaimProtected =>
            StatusCodes.Status403Forbidden,
        RefusalCode.NotFound or RefusalCode.Deleted => StatusCodes.Status404NotFound,
        RefusalCode.ClaimHeld or RefusalCode.ClaimLost or RefusalCode.IdempotencyMismatch or RefusalCode.ReleaseExists
            or RefusalCode.EmailExists or RefusalCode.LastAdministrator =>
            StatusCodes.Status409Conflict,
        RefusalCode.SecretExpired => StatusCodes.Status410Gone,
        RefusalCode.Stale => StatusCodes.Status412PreconditionFailed,
        RefusalCode.Transition or RefusalCode.Cycle or RefusalCode.HasIssues or RefusalCode.OneLevel
            or RefusalCode.OtherProject or RefusalCode.EpicInherited or RefusalCode.HasSubIssues or RefusalCode.UnknownLabel
            or RefusalCode.InPublishedRelease =>
            StatusCodes.Status422UnprocessableEntity,
        RefusalCode.WaitTooLong or RefusalCode.TooMany => StatusCodes.Status422UnprocessableEntity,
        RefusalCode.SmtpNotConfigured => StatusCodes.Status422UnprocessableEntity,
        RefusalCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a status."),
    };

    public static string TitleOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation => "A field is missing, malformed or over its limit",
        RefusalCode.UnknownField => "The request contains a field this object does not define",
        RefusalCode.CursorInvalid => "The cursor does not fit this request",
        RefusalCode.WaitTooLong => "The requested wait exceeds one hour",
        RefusalCode.TooMany => "The bulk request contains too many issues",
        RefusalCode.Unauthenticated => "No token, an unknown token, or a revoked one",
        RefusalCode.Csrf => "The browser request failed its CSRF check",
        RefusalCode.Forbidden => "The identity may not do this",
        RefusalCode.ClaimProtected => "Only a user takes over a user's claim",
        RefusalCode.NotFound => "Nothing by that key or id",
        RefusalCode.Deleted => "The issue is deleted and can still be restored",
        RefusalCode.ClaimHeld => "The issue is claimed by somebody else",
        RefusalCode.ClaimLost => "The claim has expired and somebody else holds the issue now",
        RefusalCode.IdempotencyMismatch => "The Idempotency-Key was used for a different request",
        RefusalCode.Stale => "The object has changed since it was read",
        RefusalCode.Transition => "The status does not allow this act",
        RefusalCode.Cycle => "The blocker would close a cycle",
        RefusalCode.HasIssues => "The epic still has issues",
        RefusalCode.OneLevel => "Sub-issues are exactly one level deep",
        RefusalCode.OtherProject => "The parent belongs to another project",
        RefusalCode.EpicInherited => "A sub-issue inherits its epic",
        RefusalCode.HasSubIssues => "The issue still has sub-issues",
        RefusalCode.ReleaseExists => "The project already has a release with that name",
        RefusalCode.InPublishedRelease => "The issue is in a published release",
        RefusalCode.UnknownLabel => "The project has no such label",
        RefusalCode.SmtpNotConfigured => "Transactional email is not configured",
        RefusalCode.EmailExists => "That email address already belongs to a user",
        RefusalCode.SecretExpired => "The one-time link is expired or has already been used",
        RefusalCode.LastAdministrator => "The instance must keep one active administrator",
        RefusalCode.Internal => "Something went wrong on the server",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a title."),
    };

    /// <summary>The document for <paramref name="code"/> on <paramref name="instance"/>.</summary>
    public static ProblemDetails Document(
        RefusalCode code,
        string? detail,
        string? instance,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var document = new ProblemDetails
        {
            Type = TypeOf(code),
            Title = TitleOf(code),
            Status = StatusOf(code),
            Detail = detail,
            Instance = instance,
        };

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                document.Extensions[key] = value;
            }
        }

        return document;
    }

    /// <summary>
    /// The document as an endpoint's result, for the refusals an endpoint makes
    /// itself rather than lets out of an act.
    /// </summary>
    public static IResult Result(
        RefusalCode code, string? detail, IReadOnlyDictionary<string, object?>? extensions = null) =>
        Results.Problem(Document(code, detail, instance: null, extensions));

    /// <summary>
    /// The <c>validation</c> document: <c>errors</c> maps field to messages.
    /// </summary>
    public static IResult Validation(IReadOnlyDictionary<string, string[]> errors) =>
        Results.Problem(Document(Refusal.Validation(errors)));

    /// <inheritdoc cref="Validation(IReadOnlyDictionary{string, string[]})"/>
    public static IResult Validation(string field, string message) =>
        Results.Problem(Document(Refusal.Validation(field, message)));

    /// <summary>
    /// Writes the document straight to the response, for the two refusals that
    /// happen before an endpoint runs: the challenge and the forbid.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, RefusalCode code, string? detail)
    {
        var document = Document(code, detail, context.Request.Path);

        context.Response.StatusCode = document.Status!.Value;
        await context.Response.WriteAsJsonAsync(document, options: null, ContentType, context.RequestAborted);
    }

    /// <summary>Writes a refusal's document straight to the response — for a middleware that answers in the endpoint's stead.</summary>
    public static async Task WriteAsync(HttpContext context, Refusal refusal)
    {
        var document = Document(refusal, context.Request.Path);
        context.Response.StatusCode = document.Status!.Value;
        await context.Response.WriteAsJsonAsync(document, options: null, ContentType, context.RequestAborted);
    }

    private static ProblemDetails Document(Refusal refusal, string? instance = null) =>
        Document(refusal.Code, refusal.Detail, instance, refusal.Extensions);

    /// <summary>
    /// What turns a <see cref="Refusal"/> thrown by an act into its document,
    /// and anything else into <c>internal</c> with nothing else in it.
    /// </summary>
    public sealed class Handler(ILogger<Handler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            var document = exception switch
            {
                Refusal refusal => Document(refusal, context.Request.Path),
                _ => Document(RefusalCode.Internal, detail: null, context.Request.Path),
            };

            if (document.Status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = document.Status!.Value;
            await context.Response.WriteAsJsonAsync(document, options: null, ContentType, cancellationToken);

            return true;
        }
    }
}
