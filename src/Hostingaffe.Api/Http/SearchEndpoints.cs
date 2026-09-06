using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// The one composed read of the record (<c>docs/api.md</c>, Searching): a port
/// number, an address, a name — "where was that again", asked once.
/// </summary>
/// <remarks>
/// It sits beside the objects rather than under one, because it is about all of
/// them. Postgres full text and nothing beside it: no second index, nothing to
/// operate, which is part of the promise that an instance starts from a Compose
/// file.
/// </remarks>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/search", (string? q, int? limit, Search search, CancellationToken cancellationToken) =>
                search.ExecuteAsync(q, limit, cancellationToken))
            .RequireAuthorization()
            .WithName("Search")
            .WithSummary(
                "Every live record the words match: the fields of machines, software and installations, the versions, "
                + "refs, tickets and notes of deployments, the paths and current contents of files, and the titles and "
                + "bodies of pages. A port number is looked up in the ports as well. Each hit says what was found, "
                + "where it lives, and which surface matched. Not paginated and capped instead — `limit` defaults to 50 "
                + "and never exceeds 200.")
            .Produces<IReadOnlyList<SearchHitShape>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
