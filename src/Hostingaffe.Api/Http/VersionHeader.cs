using Hostingaffe.Api.Hosting;

namespace Hostingaffe.Api.Http;

/// <summary>
/// <c>Hostingaffe-Version</c> on every response (ADR 0011), the refused and the
/// failed ones included: skew is reported by the CLI from whatever answer it
/// got, and a 401 is an answer.
/// </summary>
public static class VersionHeader
{
    public static IApplicationBuilder UseHostingaffeVersion(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[InstanceVersion.Header] = InstanceVersion.Value;
                return Task.CompletedTask;
            });

            return next(context);
        });
}
