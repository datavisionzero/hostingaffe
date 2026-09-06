using System.Text.Json;

namespace Hostingaffe.Domain;

/// <summary>
/// How a closed set's value is spelled — <c>in_progress</c> for
/// <c>InProgress</c>, <c>amd64</c> for <c>Amd64</c>. The contract, the column,
/// the history and every filter use it, and this is the one rule that produces
/// it and the one that reads it back.
/// </summary>
/// <remarks>
/// Reading it back is here rather than left to the framework because the
/// framework reads the CLR name: a filter of <c>?status=planned</c> would bind
/// only as <c>Planned</c>, and a set with a two-word value would not bind at
/// all. A filter spells its value the way the contract spells it.
/// </remarks>
public static class Spelling
{
    public static string Of<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);

    public static bool TryRead<T>(string? spelled, out T value) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(Of(candidate), spelled, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The value a filter or a path spelled, or nothing where it spelled
    /// nothing.
    /// </summary>
    /// <exception cref="ArgumentException">The word is not one of the set; the message lists them.</exception>
    public static T? Read<T>(string? spelled, string field) where T : struct, Enum
    {
        var trimmed = spelled?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return TryRead<T>(trimmed, out var value)
            ? value
            : throw new ArgumentException(
                $"A {field} is one of {string.Join(", ", Enum.GetValues<T>().Select(Of))}.", field);
    }
}
