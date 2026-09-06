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
            throw Refusal.Validation(field, refusal.Message);
        }
    }
}
