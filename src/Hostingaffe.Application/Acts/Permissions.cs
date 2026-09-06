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
    /// <summary>
    /// The note a write carried, checked where it arrived. It is the query
    /// parameter <c>note</c> on every write (ADR 0004), so the refusal names
    /// <c>note</c> and a caller can find it.
    /// </summary>
    /// <exception cref="Refusal"><c>validation</c> on <c>note</c>.</exception>
    public static string? Note(string? given) => Field("note", () => Fields.Note(given));

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

    /// <inheritdoc cref="Field{T}(string, Func{T})"/>
    public static async Task<T> FieldAsync<T>(string field, Func<Task<T>> normalize)
    {
        ArgumentNullException.ThrowIfNull(normalize);

        try
        {
            return await normalize();
        }
        catch (ArgumentException refusal)
        {
            throw Refusal.Validation(field, Said(refusal));
        }
        catch (Refusal refusal) when (refusal.Code is RefusalCode.NotFound)
        {
            // A key that names nothing arrived in a field, not in an address:
            // the caller sent a value the instance does not know, and that is
            // `validation` on the field they sent it in.
            throw Refusal.Validation(field, refusal.Detail);
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
