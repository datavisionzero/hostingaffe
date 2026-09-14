using Hostingaffe.Application.Acts;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Reports (<c>docs/api.md</c>): what a machine said about itself, at a moment.
/// </summary>
/// <remarks>
/// <para>
/// They live under their machine, because that is what gives the number an
/// address and what the token is bound to. The one write is behind a door of
/// its own — the machine's token and nothing else (ADR 0016) — and the reads
/// are behind the ordinary one, where a machine token reaches nothing.
/// </para>
/// <para>
/// A report changes nothing. No field of the machine is set, no history row is
/// written, and the answer to a delivery is the number and the moment it
/// arrived, which is all a cron has any use for (ADR 0015).
/// </para>
/// </remarks>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder endpoints)
    {
        var door = endpoints.MapGroup("/machines/{key}/reports");

        door.MapPost(
                string.Empty,
                async (string key, HandInReportRequest? request, HandInReport hand, CancellationToken cancellationToken) =>
                {
                    var receipt = await hand.ExecuteAsync(
                        key,
                        request ?? new HandInReportRequest(null, null, null, null, null, null, null, null, null),
                        cancellationToken);

                    return Results.Created(
                        $"{Routes.Api}/machines/{key}/reports/{receipt.Number}", receipt);
                })
            .RequireAuthorization(MachineTokenAuthentication.Policy)
            .WithMetadata(new BodyLimit(ReportWrites.MaxBodyBytes))
            .WithName("HandInReport")
            .WithSummary("Hand in a report for the machine the presented machine token belongs to. Authenticated with that token and with nothing else: a user token and an agent token are refused here, because someone who could post a report by hand could forge the drift comparison. Every section may be left out; what the collector could not determine goes in `missing`, with its reason. At most one report per machine per minute, and at most 64 KB.")
            .Produces<ReportReceiptShape>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        var reading = endpoints.MapGroup("/machines/{key}/reports")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        reading.MapGet(
                string.Empty,
                (string key, int? limit, int? offset, ListReports list, CancellationToken cancellationToken) =>
                    list.ExecuteAsync(key, limit, offset, cancellationToken))
            .WithName("ListReports")
            .WithSummary("The machine's reports, newest by `received_at` first, as the slim summary a list makes a line of. `limit` defaults to 50 and never exceeds 200; `offset` walks back. A machine that has never reported answers with an empty list, which is the ordinary state of one on which no cron has been set up.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        reading.MapGet("/latest", (string key, ReadLatestReport read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, cancellationToken))
            .WithName("ReadLatestReport")
            .WithSummary("The machine's latest report, whole. A machine that has never reported is `not-found` here.");

        reading.MapGet("/{number:int}", (string key, int number, ReadReport read, CancellationToken cancellationToken) =>
                read.ExecuteAsync(key, number, cancellationToken))
            .WithName("ReadReport")
            .WithSummary("One report by its number, whole. A number the sweep has taken is `not-found`, and numbers are never handed out twice.");

        return endpoints;
    }
}
