using System.Globalization;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The concurrency guard on a text two writers share
/// (<c>docs/api.md</c>, "Guarding a write"): the client sends back what it read
/// in <c>If-Match</c>, and a write over somebody else's is refused as
/// <c>stale</c> rather than silently winning.
/// </summary>
/// <remarks>
/// What it sends back depends on what the record actually keeps. A file is
/// numbered — every write is a revision and every revision is readable — so its
/// entity tag is that number. Everything else has only the moment it last
/// moved, so its entity tag is <c>updated_at</c>. What has revisions is guarded
/// with the revision; what has none is guarded with the stand.
/// </remarks>
public static class GuardedWrite
{
    /// <summary>
    /// The revision an <c>If-Match</c> carries on a file, or <c>null</c> where
    /// the caller sent none and accepts whatever is there.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c> when the header is not a revision number.</exception>
    public static int? ExpectedRevision(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        var text = ifMatch.Trim().Trim('"');
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw Refusal.Validation("If-Match", "On a file, If-Match carries the revision as it was read, quoted: a number, counting from 1.");
    }

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
