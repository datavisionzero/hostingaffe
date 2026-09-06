namespace Hostingaffe.Domain;

/// <summary>Which kind of thing a key was given to (<c>CONTEXT.md</c>, Key).</summary>
public enum Keyed
{
    Machine,
    Software,
    Installation,
}

/// <summary>
/// That a key was given out, once, and that it is therefore spent forever
/// (<c>CONTEXT.md</c>, Key). One row per kind and key, written when the thing
/// is created and never touched again.
/// </summary>
/// <remarks>
/// <para>
/// While a deleted row is in its grace period it holds its own key and this
/// register says nothing new. After the purge the row is gone, and without
/// something that remembers, the key would be free again — which VISION 7 rules
/// out.
/// </para>
/// <para>
/// It is a register and not a second history and not a bin: two columns, one
/// question, and a unique index that <em>enforces</em> the rule rather than
/// leaving it to a query somebody can forget. The history is not that register,
/// because the history is there to be read and would have to carry a uniqueness
/// rule no index could hold.
/// </para>
/// </remarks>
public sealed class AssignedKey
{
    private AssignedKey()
    {
        // EF Core materializes through this; every other route goes through To.
    }

    private AssignedKey(Keyed kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    public Keyed Kind { get; private init; }

    public string Key { get; private init; } = null!;

    /// <exception cref="ArgumentException">The key does not hold, or the kind is not one.</exception>
    public static AssignedKey To(Keyed kind, string key) =>
        Enum.IsDefined(kind)
            ? new AssignedKey(kind, Domain.Key.Normalize(key))
            : throw new ArgumentException("Not a kind of thing that has a key.", nameof(kind));
}
