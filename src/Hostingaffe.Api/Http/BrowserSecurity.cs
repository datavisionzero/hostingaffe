using System.Collections.Concurrent;

namespace Hostingaffe.Api.Http;

public sealed record BrowserCookie(string Name, bool Secure)
{
    public const string ProductionName = "__Host-hostingaffe_session";
    public const string DevelopmentName = "hostingaffe_session";
    public static BrowserCookie For(bool development) => development ? new(DevelopmentName, false) : new(ProductionName, true);
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
