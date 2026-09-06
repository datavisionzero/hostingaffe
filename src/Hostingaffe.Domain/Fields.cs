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

    /// <summary>One line, trimmed, at most <paramref name="maxLength"/> characters.</summary>
    /// <exception cref="ArgumentException">It spans lines or is too long.</exception>
    public static string Line(string value, int maxLength, string what) =>
        value is null || value.Length > maxLength || value.Contains('\n')
            ? throw new ArgumentException($"{what} is one line of at most {maxLength} characters.")
            : value;

    /// <summary>How a timestamp reads in a history row: the same microseconds the API writes.</summary>
    public static string? Stamp(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}
