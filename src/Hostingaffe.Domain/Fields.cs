using System.Globalization;
using Hostingaffe.Domain.History;

namespace Hostingaffe.Domain;

/// <summary>
/// The shapes every editable field of the record shares: how a text arrives,
/// how a closed set is set, and how a timestamp is spelled when the history
/// writes it down.
/// </summary>
/// <remarks>
/// It sits at the root because a machine, a software, an installation and a
/// deployment all apply their fields the same way, and four copies of these
/// twenty lines would be three that drift. Each of them answers with the
/// <see cref="FieldChange"/> list its history is written from
/// (<c>docs/storage.md</c>, The history).
/// </remarks>
public static class Fields
{
    /// <summary>What a URL fits in, wherever one is stored.</summary>
    public const int UrlMaxLength = 500;

    /// <summary>What the note beside a change fits in.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>What a path on a machine fits in, wherever one is stored.</summary>
    public const int PathMaxLength = 500;

    /// <summary>
    /// The note a write carries into the history: why, said beside what
    /// changed (VISION 6.1). One line, trimmed, and nothing at all where there
    /// was nothing to say — a note is offered, never demanded, and an empty one
    /// is the same as none.
    /// </summary>
    /// <exception cref="ArgumentException">It spans lines or is too long.</exception>
    public static string? Note(string? given) =>
        given?.Trim() is { Length: > 0 } said ? Line(said, NoteMaxLength, "A note") : null;


    /// <summary>
    /// One free-text value: absent leaves the field alone, the empty string
    /// clears it, anything else is normalized and set (<c>docs/api.md</c>,
    /// Conventions).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value does not hold; <see cref="ArgumentException.ParamName"/> is
    /// <paramref name="field"/>, so the refusal names the field the caller sent.
    /// </exception>
    public static void Text(
        string field,
        string? given,
        string? current,
        Action<string?> set,
        Func<string, string> normalize,
        List<FieldChange> changes)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(changes);

        if (given is null)
        {
            return;
        }

        var trimmed = given.Trim();
        string? value;

        try
        {
            value = trimmed.Length == 0 ? null : normalize(trimmed);
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(refusal.Message, field);
        }

        if (value == current)
        {
            return;
        }

        set(value);
        changes.Add(new FieldChange(field, current, value));
    }

    /// <summary>
    /// One value of a closed set: absent leaves it, and there is no clearing —
    /// a closed set has no empty value to send.
    /// </summary>
    public static void Closed<T>(
        string field,
        T? given,
        T? current,
        Action<T> set,
        List<FieldChange> changes)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(changes);

        if (given is not { } value || (current is { } held && held.Equals(value)))
        {
            return;
        }

        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException($"Not a {field}.", field);
        }

        set(value);
        changes.Add(new FieldChange(field, current is { } was ? Spelling.Of(was) : null, Spelling.Of(value)));
    }

    /// <summary>
    /// A whole list at once: absent leaves it alone, an empty list clears it,
    /// and anything else replaces it. A list is not patched entry by entry —
    /// there is no address for an entry, and a caller who sends two of them
    /// means both.
    /// </summary>
    /// <param name="spell">
    /// How one entry reads, for the history and for telling two lists apart.
    /// </param>
    /// <param name="identity">
    /// What makes two entries the same entry, where that is narrower than how
    /// they read — two ports on the same number and protocol are one port,
    /// whatever their scope says.
    /// </param>
    /// <exception cref="ArgumentException">
    /// An entry does not hold, the same entry arrives twice, or there are more
    /// than <paramref name="maxCount"/> of them.
    /// </exception>
    public static void Many<T>(
        string field,
        IReadOnlyList<T>? given,
        IReadOnlyList<T> current,
        Action<IReadOnlyList<T>> set,
        Func<T, string> spell,
        int maxCount,
        List<FieldChange> changes,
        Func<T, string>? identity = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(changes);

        if (given is null)
        {
            return;
        }

        if (given.Count > maxCount)
        {
            throw new ArgumentException($"A {field} list holds at most {maxCount} entries.", field);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in given)
        {
            if (!seen.Add((identity ?? spell)(entry)))
            {
                throw new ArgumentException($"{spell(entry)} is in {field} twice.", field);
            }
        }

        var was = Joined(current, spell);
        var becomes = Joined(given, spell);

        if (was == becomes)
        {
            return;
        }

        set(given);
        changes.Add(new FieldChange(field, was, becomes));
    }

    /// <summary>
    /// Whatever <paramref name="value"/> answers, and whatever it refuses said
    /// against <paramref name="field"/> — for the checks that run over a whole
    /// list, where the entry knows what is wrong with it and only the caller
    /// knows which field it arrived in.
    /// </summary>
    public static T InField<T>(string field, Func<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            return value();
        }
        catch (ArgumentException refusal)
        {
            throw new ArgumentException(refusal.Message, field);
        }
    }

    /// <summary>One line, trimmed, at most <paramref name="maxLength"/> characters.</summary>
    /// <exception cref="ArgumentException">It spans lines or is too long.</exception>
    public static string Line(string value, int maxLength, string what) =>
        value is null || value.Length > maxLength || value.Contains('\n')
            ? throw new ArgumentException($"{what} is one line of at most {maxLength} characters.")
            : value;

    /// <summary>
    /// A place on the machine: absolute, one line, and it does not climb. What
    /// the place <em>is</em> stays the field's own business — the directory an
    /// installation lives in, the directory its data lies in, the file a
    /// secret's value lies in — and <paramref name="said"/> is how that field
    /// says what it wanted.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an absolute path, or it climbs.</exception>
    public static string Absolute(string? path, string said)
    {
        // One trailing slash is what a person types and means nothing; the root
        // is the one path that is a slash.
        var trimmed = path?.Trim() ?? string.Empty;
        Line(trimmed, PathMaxLength, "A path");

        return !trimmed.StartsWith('/')
            ? throw new ArgumentException(said)
            : trimmed.Split('/').Any(segment => segment is "..")
                ? throw new ArgumentException("A path does not climb; .. is not part of one.")
                : trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    /// <summary>
    /// An absolute <c>http</c> or <c>https</c> address. Nothing else is a
    /// homepage, a repository or a URL an installation is reachable at, and a
    /// half-typed one is refused where it was typed rather than found broken by
    /// whoever clicks it.
    /// </summary>
    /// <exception cref="ArgumentException">It is not an absolute http(s) URL.</exception>
    public static string Url(string value, string what)
    {
        Line(value ?? string.Empty, UrlMaxLength, $"A {what}");

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(parsed.Host)
            ? value!
            : throw new ArgumentException($"A {what} is an http or https address.");
    }

    /// <summary>
    /// How a list reads in a history row: its entries, in order, separated by a
    /// comma — and nothing at all where there are none, so that clearing a list
    /// records a value that is empty rather than a string that looks it.
    /// </summary>
    public static string? Joined<T>(IReadOnlyList<T> entries, Func<T, string> spell) =>
        entries is { Count: > 0 } ? string.Join(", ", entries.Select(spell)) : null;

    /// <summary>How a timestamp reads in a history row: the same microseconds the API writes.</summary>
    public static string? Stamp(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}
