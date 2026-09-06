using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The cursor of a page (<c>docs/api.md</c>, Pagination): opaque to the client,
/// the sort key and id of the last item plus a fingerprint of the filters and
/// the sort it was issued for, so that a cursor handed to a different request
/// is refused rather than paging through the wrong list.
/// </summary>
public static class IssueCursor
{
    private sealed record Payload(string F, DateTimeOffset? T, short? P, int N, Guid I, string? K = null, int? E = null);

    public static string Encode(IssueQuery query, IssueSort sort, SortOrder order, IssueRow last)
    {
        var payload = new Payload(
            Fingerprint(query, sort, order),
            sort is IssueSort.Updated ? last.UpdatedAt : last.CreatedAt,
            sort is IssueSort.Priority or IssueSort.Epic ? (short)last.Priority : null,
            last.Number,
            last.Id,
            // The epic key, in the two halves the comparison chain uses. A row
            // under no epic carries no number, which is the extreme value at
            // the far end of that chain rather than a missing one.
            sort is IssueSort.Epic ? last.ProjectKey : null,
            sort is IssueSort.Epic ? last.EpicNumber : null);

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <exception cref="Refusal"><c>cursor-invalid</c>.</exception>
    public static IssuePosition Decode(string cursor, IssueQuery query, IssueSort sort, SortOrder order)
    {
        Payload? payload;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            payload = JsonSerializer.Deserialize<Payload>(Convert.FromBase64String(base64));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            payload = null;
        }

        if (payload is null || payload.F != Fingerprint(query, sort, order))
        {
            throw new Refusal(RefusalCode.CursorInvalid, "The cursor is not one this server issued for these filters and this sort.");
        }

        return new IssuePosition(
            payload.T, payload.P is { } p ? (Priority)p : null, payload.N, payload.I, payload.K ?? string.Empty, payload.E);
    }

    private static string Fingerprint(IssueQuery query, IssueSort sort, SortOrder order)
    {
        var text = JsonSerializer.Serialize(new { query, sort, order });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
