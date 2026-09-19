using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// What happened lately (<c>docs/api.md</c>, The history): the history of every
/// subject in one reading, with the deployments mixed in.
/// </summary>
/// <remarks>
/// It sits beside the objects rather than under one, like the search, because
/// it is about all of them. What a subject's own <c>…/history</c> answers is
/// unchanged and stays what it is: every row about that one thing, oldest
/// first. This is the other question — not "what became of this", but "what has
/// been going on".
/// </remarks>
public static class HistoryEndpoints
{
    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/history",
                (string? machine, string? kind, string? before, int? limit,
                 ReadHistory read, CancellationToken cancellationToken) =>
                    read.ExecuteAsync(machine, kind, before, limit, cancellationToken))
            .RequireAuthorization()
            .WithName("ReadHistory")
            .WithSummary(
                "Every change to the record, newest first, with the deployments mixed in. The rows one act wrote are "
                + "one event carrying the fields it changed, and a deployment is an event whose one change is the "
                + "version. `machine` keeps what belongs to one machine — its own changes and those of its "
                + "installations, their deployments, the files of both and the pages attached to them — and `kind` "
                + "keeps one kind of subject. `limit` defaults to 50 and never exceeds 200; `before` is the `cursor` "
                + "of the last event of the page before this one. A subject that has been deleted keeps its events: "
                + "the last thing that happened to it is that somebody deleted it.")
            .Produces<IReadOnlyList<HistoryEventShape>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
