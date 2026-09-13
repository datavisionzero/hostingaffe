using Microsoft.AspNetCore.Http.Features;
using Hostingaffe.Domain;

namespace Hostingaffe.Api.Http;

/// <summary>
/// What an endpoint says its body may weigh, and the middleware that holds it
/// to that before anything is read (<c>docs/api.md</c>, Errors).
/// </summary>
/// <remarks>
/// It sits in front of model binding rather than in a filter behind it, because
/// a filter is handed a body that has already been read: refusing afterwards
/// would have cost exactly what the limit exists to avoid. A declared
/// <c>Content-Length</c> is answered at once; a chunked body is capped by the
/// server, which then refuses it in the same breath.
/// </remarks>
public sealed record BodyLimit(int Bytes);

/// <inheritdoc cref="BodyLimit"/>
public static class BodyLimits
{
    public static IApplicationBuilder UseHostingaffeBodyLimit(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<BodyLimit>() is { } limit)
            {
                if (context.Request.ContentLength > limit.Bytes)
                {
                    await Problems.WriteAsync(
                        context,
                        new Refusal(
                            RefusalCode.TooLarge,
                            $"This endpoint takes a body of at most {limit.Bytes} bytes, and this one is {context.Request.ContentLength}.",
                            new Dictionary<string, object?> { ["limit"] = limit.Bytes }));

                    return;
                }

                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                {
                    feature.MaxRequestBodySize = limit.Bytes;
                }
            }

            await next(context);
        });
    }
}
