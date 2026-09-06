using System.Globalization;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The concurrency guard on a text two writers share
/// (<c>docs/api.md</c>, "Concurrency on text fields"): the client sends back
/// the <c>updated_at</c> it read, and a write over somebody else's is refused
/// as <c>stale</c> rather than silently winning.
/// </summary>
public static class GuardedWrite
{
    /// <summary>
    /// The version an <c>If-Match</c> header carries, or <c>null</c> where the
    /// caller sent none and accepts whatever is there.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c> when the header is not a timestamp.</exception>
    public static DateTimeOffset? Expected(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        var text = ifMatch.Trim().Trim('"');
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw Refusal.Validation("If-Match", "If-Match carries the updated_at as it was read, quoted.");
    }
}
