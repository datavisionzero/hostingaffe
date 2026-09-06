using Hostingaffe.Application.Ports;
using Serilog;
using Serilog.Events;

namespace Hostingaffe.Api.Hosting;

/// <summary>
/// The two sinks of ADR 0008, wired from the first start: logaffe is the
/// intended target, enabled as soon as an endpoint and a token are configured;
/// Serilog to the console and a rolling file is what an installation gets
/// otherwise. No call site knows which one is active — everything logs through
/// <c>ILogger</c>.
/// </summary>
/// <remarks>
/// <para>
/// The console is always on: a container's log is where an operator looks first,
/// whichever sink is the target. The file is the operator's way to keep
/// something without running anything, and it is bounded — a day per file,
/// seven files — because an unbounded log next to the database is the wrong
/// kind of surprise.
/// </para>
/// <para>
/// A logaffe that cannot be reached never becomes an outage: the sink queues in
/// memory, drops the oldest under pressure and reports what it could not deliver
/// to Serilog's <c>SelfLog</c>, which goes to standard error here. Nothing about
/// a request body is logged; the request log carries method, path, status and
/// duration and nothing that an agent wrote (VISION 13).
/// </para>
/// </remarks>
public static class LogSinks
{
    /// <summary>Where the rolling file goes, relative to the working directory — <c>/app</c> in the image.</summary>
    public const string FilePath = "logs/hostingaffe-.log";

    public static LoggerConfiguration Configure(LoggerConfiguration configuration, LogSettings settings)
    {
        var level = Enum.Parse<LogEventLevel>(settings.Level, ignoreCase: true);

        configuration
            .MinimumLevel.Is(level)
            // The framework's own chatter stays out unless the floor is lowered
            // deliberately; the product's lines are the ones that matter.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("application", "hostingaffe")
            .Enrich.WithProperty("version", InstanceVersion.Value)
            .WriteTo.Console();

        if (settings.ShipsToLogaffe)
        {
            configuration.WriteTo.Logaffe(settings.Endpoint!, settings.Token!);
        }
        else
        {
            configuration.WriteTo.File(
                FilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: false);
        }

        return configuration;
    }
}
