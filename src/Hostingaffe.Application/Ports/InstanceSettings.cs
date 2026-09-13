namespace Hostingaffe.Application.Ports;

/// <summary>
/// The dials an operator sets, once per instance: how long a deleted row can be
/// restored before the purge may take it (ADR 0013), and how long a machine's
/// reports are kept (ADR 0015).
/// </summary>
/// <remarks>
/// Both have a default that takes effect without anyone touching a file, which
/// is the promise a released trunk makes to whoever already runs it: `docker
/// compose pull && up -d` and the instance goes on working.
/// </remarks>
public sealed record InstanceSettings(TimeSpan DeletionGrace, TimeSpan ReportRetention)
{
    public const string DeletionGraceVariable = "HOSTINGAFFE_DELETION_GRACE_DAYS";

    public const string ReportRetentionVariable = "HOSTINGAFFE_REPORT_RETENTION_DAYS";

    public static readonly InstanceSettings Defaults = new(TimeSpan.FromDays(7), TimeSpan.FromDays(30));

    /// <summary>
    /// Whether reports are swept at all. Zero keeps every one of them, for
    /// whoever wants that, and the example file says what a year of it costs.
    /// </summary>
    public bool SweepsReports => ReportRetention > TimeSpan.Zero;

    /// <exception cref="ArgumentException">A variable is set and is not the number it has to be.</exception>
    public static InstanceSettings FromVariables(string? deletionGraceDays, string? reportRetentionDays = null) =>
        new(
            Read(deletionGraceDays, DeletionGraceVariable, Defaults.DeletionGrace, TimeSpan.FromDays),
            ReadRetention(reportRetentionDays));

    /// <summary>
    /// The one dial that takes a zero, because "keep everything" is a thing an
    /// operator may mean and a grace period of nothing is not.
    /// </summary>
    private static TimeSpan ReadRetention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Defaults.ReportRetention;
        }

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            && number >= 0
            ? TimeSpan.FromDays(number)
            : throw new ArgumentException(
                $"{ReportRetentionVariable} is '{value}'; it has to be a number of days, or 0 to keep every report.",
                ReportRetentionVariable);
    }

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
