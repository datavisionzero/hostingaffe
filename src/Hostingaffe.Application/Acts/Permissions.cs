using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// The permission line of VISION 9 (<c>docs/api.md</c>, Who may do what): every
/// identity reads everything and writes content; an agent administers no
/// identities (planaffe ADR 0015), and an administrator manages users, agents
/// and tokens.
/// </summary>
public static class Permissions
{
    /// <exception cref="Refusal"><c>forbidden</c> when the caller is an agent.</exception>
    public static Caller RequireUser(this Caller caller, string act) =>
        caller.IsUser
            ? caller
            : throw new Refusal(RefusalCode.Forbidden, $"Only a user may {act}; an agent may not (ADR 0015).");

    /// <exception cref="Refusal"><c>forbidden</c> when the caller does not administer the instance.</exception>
    public static Caller RequireAdministrator(this Caller caller, string act) =>
        caller.RequireUser(act).Administrator
            ? caller
            : throw new Refusal(RefusalCode.Forbidden, $"Only an administrator may {act}.");
}

/// <summary>
/// Turns what the Domain refuses about one field into the <c>validation</c>
/// refusal that names the field.
/// </summary>
public static class Validated
{
    /// <exception cref="Refusal"><c>validation</c> on <paramref name="field"/>.</exception>
    public static T Field<T>(string field, Func<T> normalize)
    {
        try
        {
            return normalize();
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(field, Said(refusal));
        }
    }

    /// <summary>
    /// What the exception says, without the <c>(Parameter 'x')</c> the runtime
    /// appends: the document already names the field, and saying it twice reads
    /// like a stack trace. Public because an act that catches its own
    /// <see cref="ArgumentException"/> owes the caller the same sentence.
    /// </summary>
    public static string Said(ArgumentException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        var message = refusal.Message;
        var appended = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return appended < 0 ? message : message[..appended];
    }
}
