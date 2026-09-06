namespace Hostingaffe.Application.Ports;

/// <summary>
/// The three variables of ADR 0008, and nothing else: where logaffe answers, the
/// token that names the stream there, and the floor. logaffe is the target as
/// soon as the first two are set; otherwise the instance logs to the console
/// and a rolling file.
/// </summary>
public sealed record LogSettings(Uri? Endpoint, string? Token, string Level)
{
    public const string EndpointVariable = "HOSTINGAFFE_LOG_ENDPOINT";

    public const string TokenVariable = "HOSTINGAFFE_LOG_TOKEN";

    public const string LevelVariable = "HOSTINGAFFE_LOG_LEVEL";

    public const string DefaultLevel = "Information";

    public static readonly string[] Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>Whether entries go to logaffe: both the endpoint and the token are set.</summary>
    public bool ShipsToLogaffe => Endpoint is not null && Token is not null;

    /// <exception cref="ArgumentException">
    /// One of the pair without the other, an endpoint that is not an absolute
    /// http(s) address, or a level that is not one of Serilog's.
    /// </exception>
    public static LogSettings FromVariables(string? endpoint, string? token, string? level)
    {
        var hasEndpoint = !string.IsNullOrWhiteSpace(endpoint);
        var hasToken = !string.IsNullOrWhiteSpace(token);

        if (hasEndpoint != hasToken)
        {
            throw new ArgumentException(
                $"{EndpointVariable} and {TokenVariable} go together: set both to log into logaffe, or neither to log to the console and a file.",
                hasEndpoint ? TokenVariable : EndpointVariable);
        }

        Uri? address = null;
        if (hasEndpoint)
        {
            if (!Uri.TryCreate(endpoint!.Trim(), UriKind.Absolute, out address) || address.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException($"{EndpointVariable} is '{endpoint}'; it has to be an absolute http or https address.", EndpointVariable);
            }
        }

        var chosen = string.IsNullOrWhiteSpace(level) ? DefaultLevel : level.Trim();
        var known = Levels.FirstOrDefault(l => l.Equals(chosen, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{LevelVariable} is '{level}'; it is one of {string.Join(", ", Levels)}.", LevelVariable);

        return new LogSettings(address, hasToken ? token!.Trim() : null, known);
    }
}
