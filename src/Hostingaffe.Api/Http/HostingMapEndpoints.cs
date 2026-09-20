using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

public static class HostingMapEndpoints
{
    public static IEndpointRouteBuilder MapHostingMap(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/hosting-map", (ReadHostingMap read, CancellationToken ct) => read.ExecuteAsync(ct))
            .RequireAuthorization()
            .WithName("ReadHostingMap")
            .WithSummary("Providers and live machines with effective provider keys and recorded IP addresses, in one response for the read-only hosting map.")
            .Produces<HostingMapShape>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return endpoints;
    }
}
