using System.Collections.Concurrent;

namespace Hostingaffe.Api.Http;

/// <summary>The cookie a browser session travels in, as the request's own scheme allows.</summary>
/// <remarks>
/// Derived from the request rather than from the environment, because the two
/// disagree in the case that matters: an instance is in production the moment it
/// is installed, and <c>docs/install.md</c> has the first sign-in happen over
/// <c>http://&lt;host&gt;:8080/</c>, before any proxy is in front of it. A
/// <c>__Host-</c> cookie is refused outright there and a <c>secure</c> one is
/// dropped, so the instance answered <c>204</c>, the browser kept nothing, and
/// the application came back to sign-in with nothing to say — the one failure a
/// person cannot debug from the screen.
///
/// So: over HTTPS the strict cookie, with the prefix that binds it to this host
/// and this path; over plain HTTP a cookie without the prefix and without the
/// flag, which is the only kind that can work there. What that costs is written
/// down where the choice is made — a session over plain HTTP travels in the
/// clear, and <c>docs/install.md</c> says to put TLS in front of anything that
/// is not a trial.
///
/// The scheme is the caller's, which is why <see cref="TrustedProxies"/> stands
/// in front of this: behind a proxy that terminates TLS and is trusted to say
/// so, the request is HTTPS and the strict cookie is the one that is set.
/// </remarks>
public sealed record BrowserCookie(string Name, bool Secure)
{
    /// <summary>Over HTTPS. `__Host-` binds the cookie to this host and `/`, and requires `secure`.</summary>
    public const string SecureName = "__Host-hostingaffe_session";

    /// <summary>Over plain HTTP, where a prefixed or `secure` cookie is not stored at all.</summary>
    public const string PlainName = "hostingaffe_session";

    public static BrowserCookie For(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.IsHttps ? new(SecureName, true) : new(PlainName, false);
    }

    public CookieOptions Options(DateTimeOffset expires) => new() { HttpOnly = true, Secure = Secure, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires };
}

/// <summary>Small bounded rolling-window limiter for failed password sign-ins.</summary>
public sealed class LoginThrottle(TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int AccountLimit = 5, AddressLimit = 20, MaximumKeys = 4096;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> attempts = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public bool IsBlocked(string normalizedEmail, string sourceAddress)
    {
        lock (gate) return Count("account:" + normalizedEmail) >= AccountLimit || Count("address:" + sourceAddress) >= AddressLimit;
    }
    public void Failed(string normalizedEmail, string sourceAddress)
    {
        lock (gate) { Add("account:" + normalizedEmail); Add("address:" + sourceAddress); TrimStore(); }
    }
    public void Succeeded(string normalizedEmail) { lock (gate) attempts.TryRemove("account:" + normalizedEmail, out _); }
    private int Count(string key) { if (!attempts.TryGetValue(key, out var queue)) return 0; Prune(queue); return queue.Count; }
    private void Add(string key) { var queue = attempts.GetOrAdd(key, _ => new()); Prune(queue); queue.Enqueue(clock.GetUtcNow()); }
    private void Prune(Queue<DateTimeOffset> queue) { var floor = clock.GetUtcNow() - Window; while (queue.TryPeek(out var time) && time <= floor) queue.Dequeue(); }
    private void TrimStore() { if (attempts.Count <= MaximumKeys) return; foreach (var pair in attempts.Where(x => { Prune(x.Value); return x.Value.Count == 0; }).Take(attempts.Count - MaximumKeys)) attempts.TryRemove(pair.Key, out _); }
}

/// <summary>
/// A browser write proves itself twice: the custom header, which no cross-site
/// form can set, and an <c>Origin</c> that is this instance.
/// </summary>
/// <remarks>
/// With <c>HOSTINGAFFE_PUBLIC_URL</c> set the origin is compared whole. Without it
/// the scheme is left out, because it is the one part the instance cannot know:
/// a reverse proxy that terminates TLS forwards the request as <c>http</c>
/// unless it is trusted to say otherwise (<see cref="TrustedProxies"/>), and
/// comparing that against the browser's <c>https</c> refused every write an
/// operator who had not set the variable made. The host carries the check on
/// its own — a foreign origin cannot match it, and one that could would already
/// be answering for this instance.
/// </remarks>
public static class CsrfProtection
{
    public const string Header = "X-Hostingaffe-CSRF";

    public static bool IsSafe(HttpRequest request, Uri? publicUrl)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Headers[Header].ToString() != "1"
            || !Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin))
        {
            return false;
        }

        return publicUrl is null
            ? request.Host.HasValue
                && string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            : Uri.Compare(publicUrl, origin, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
