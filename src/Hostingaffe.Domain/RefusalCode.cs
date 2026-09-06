namespace Hostingaffe.Domain;

/// <summary>
/// Every way the product says no, by the code a client switches on
/// (<c>docs/api.md</c>, Errors). One list for all of them, so that a refusal
/// thrown three layers down and one thrown at the door are the same kind of
/// thing to the CLI, which derives its exit code from this.
/// </summary>
/// <remarks>
/// The HTTP status each carries is the Api's business and is not here; the
/// spelling on the wire is the kebab case of the name — <c>ClaimHeld</c> is
/// <c>claim-held</c>. Some of these the Domain never raises —
/// <see cref="Unauthenticated"/>, <see cref="IdempotencyMismatch"/>,
/// <see cref="CursorInvalid"/> are the adapters' — and they are here anyway,
/// because a second list somewhere else would be the one nobody keeps.
/// </remarks>
public enum RefusalCode
{
    /// <summary>A field is missing, malformed or over its limit; <c>errors</c> maps field to message.</summary>
    Validation,

    /// <summary>A closed request object contains a field it does not define.</summary>
    UnknownField,

    /// <summary>The cursor does not fit the filters or is not one the server issued.</summary>
    CursorInvalid,

    /// <summary>No token, an unknown token, or a revoked one.</summary>
    Unauthenticated,

    /// <summary>A cookie-authenticated write is missing the browser CSRF proof.</summary>
    Csrf,

    /// <summary>The identity may not do this.</summary>
    Forbidden,

    /// <summary>The key or id names nothing the caller can see.</summary>
    NotFound,

    /// <summary>The object exists but is in its grace period; <c>restorable_until</c> says how long.</summary>
    Deleted,

    /// <summary>The object's state does not allow the act.</summary>
    Transition,

    /// <summary>The <c>Idempotency-Key</c> was used for a different request.</summary>
    IdempotencyMismatch,

    /// <summary><c>If-Match</c> does not match the object's <c>updated_at</c>; <c>current</c> carries the object.</summary>
    Stale,

    /// <summary>An act that necessarily sends mail was used without SMTP configuration.</summary>
    SmtpNotConfigured,

    /// <summary>An invitation or confirmed email duplicates a normalized address.</summary>
    EmailExists,

    /// <summary>A one-time identity secret is unknown, replaced, used or expired.</summary>
    SecretExpired,

    /// <summary>Deactivation or demotion would leave no active administrator.</summary>
    LastAdministrator,

    /// <summary>A bug; the response carries nothing else.</summary>
    Internal,
}
