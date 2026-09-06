namespace Hostingaffe.Application.Ports;

/// <summary>
/// The dial an operator sets, once per instance (ADR 0013): how long a deleted
/// row can be restored before the purge may take it.
/// </summary>
public sealed record InstanceSettings(TimeSpan DeletionGrace)
{
    public const string DeletionGraceVariable = "HOSTINGAFFE_DELETION_GRACE_DAYS";

    public static readonly InstanceSettings Defaults = new(TimeSpan.FromDays(7));

    /// <exception cref="ArgumentException">The variable is set and is not a positive number.</exception>
    public static InstanceSettings FromVariables(string? deletionGraceDays) =>
        new(Read(deletionGraceDays, DeletionGraceVariable, Defaults.DeletionGrace, TimeSpan.FromDays));

    private static TimeSpan Read(string? value, string variable, TimeSpan fallback, Func<double, TimeSpan> unit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? unit(number)
            : throw new ArgumentException($"{variable} is '{value}'; it has to be a positive number.", variable);
    }
}
