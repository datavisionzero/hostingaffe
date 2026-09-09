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

    /// <summary>The wire spelling of a code: <c>NotFound</c> is <c>not-found</c>.</summary>
    public static string CodeOf(RefusalCode code) => JsonNamingPolicy.KebabCaseLower.ConvertName(code.ToString());

    public static string TypeOf(RefusalCode code) => TypePrefix + CodeOf(code);

    public static int StatusOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation or RefusalCode.UnknownField or RefusalCode.CursorInvalid
            or RefusalCode.DevicePending or RefusalCode.DeviceDenied or RefusalCode.DeviceExpired =>
            StatusCodes.Status400BadRequest,
        RefusalCode.Unauthenticated => StatusCodes.Status401Unauthorized,
        RefusalCode.Csrf or RefusalCode.Forbidden => StatusCodes.Status403Forbidden,
        RefusalCode.NotFound or RefusalCode.Deleted => StatusCodes.Status404NotFound,
        RefusalCode.IdempotencyMismatch or RefusalCode.EmailExists or RefusalCode.LastAdministrator =>
            StatusCodes.Status409Conflict,
        RefusalCode.SecretExpired => StatusCodes.Status410Gone,
        RefusalCode.Stale => StatusCodes.Status412PreconditionFailed,
        RefusalCode.Transition or RefusalCode.SmtpNotConfigured => StatusCodes.Status422UnprocessableEntity,
        RefusalCode.Internal => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "A refusal code without a status."),
    };

    public static string TitleOf(RefusalCode code) => code switch
    {
        RefusalCode.Validation => "A field is missing, malformed or over its limit",
        RefusalCode.UnknownField => "The request contains a field this object does not define",
        RefusalCode.CursorInvalid => "The cursor does not fit this request",
        RefusalCode.Unauthenticated => "No token, an unknown token, or a revoked one",
        RefusalCode.Csrf => "The browser request failed its CSRF check",
        RefusalCode.Forbidden => "The identity may not do this",
        RefusalCode.NotFound => "Nothing by that key or id",
        RefusalCode.Deleted => "The object is deleted and can still be restored",
        RefusalCode.IdempotencyMismatch => "The Idempotency-Key was used for a different request",
        RefusalCode.Stale => "The object has changed since it was read",
        RefusalCode.Transition => "The object's state does not allow this act",
        RefusalCode.SmtpNotConfigured => "Transactional email is not configured",
        RefusalCode.EmailExists => "That email address already belongs to a user",
        RefusalCode.SecretExpired => "The one-time link is expired or has already been used",
        RefusalCode.DevicePending => "Nobody has approved this login yet",
        RefusalCode.DeviceDenied => "A user refused this login",
        RefusalCode.DeviceExpired => "This login is no longer waiting to be approved",
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
    /// A body or a parameter the framework could not read at all — a closed set
    /// given a word outside it, a number where a string was sent, malformed
    /// JSON. It is the caller's mistake and answers as <c>validation</c>, named
    /// after the field where the reader gave up.
    /// </summary>
    private static Refusal Unreadable(BadHttpRequestException exception)
    {
        var path = (exception.InnerException as JsonException)?.Path;

        return path is null or "$"
            ? Refusal.Validation("body", "The request body is not the JSON object this endpoint takes.")
            : Refusal.Validation(
                path.Split('.', '[')[^1].TrimEnd(']'),
                "The value is not of the type this field takes; a closed set takes one of its words.");
    }

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
                BadHttpRequestException bad => Document(Unreadable(bad), context.Request.Path),
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
