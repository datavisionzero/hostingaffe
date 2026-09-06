namespace Hostingaffe.Api.Http;

/// <summary>
/// Where the API is, as one word (ADR 0002). Every endpoint hangs under
/// <see cref="Api"/> and every other path is the web application's, which is
/// what keeps the two from fighting over `pages` — or, once the domain
/// arrives, over `machines`, `installations` and `deployments`.
/// </summary>
/// <remarks>
/// Routing applies the prefix by itself, because the endpoints are mapped into
/// a group carrying it. What needs this constant is everything routing does not
/// write: the <c>Location</c> of a created object, and the address of the
/// contract.
/// </remarks>
public static class Routes
{
    /// <summary>The first segment of every address the instance answers as an API.</summary>
    public const string Api = "/api";
}
