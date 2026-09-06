namespace Hostingaffe.Domain.Identities;

/// <summary>
/// Whoever acts. Every record of who did something — the claim, the author of
/// an issue or a comment, every history entry — points at exactly one, and an
/// identity is either a <see cref="User"/> or an <see cref="Agent"/>
/// (<c>CONTEXT.md</c>, Identity).
/// </summary>
/// <remarks>
/// <para>
/// One hierarchy in one table, so that everything that references "who" has one
/// place to point at (<c>docs/storage.md</c>). The two kinds differ in what they
/// may do and in how long a claim of theirs lives, not in what they are to the
/// rest of the model.
/// </para>
/// <para>
/// <see cref="Administrator"/> sits here rather than on the user because it is
/// asked of every caller on every request, whichever kind the caller is — and
/// for an agent the answer is always no (ADR 0015). An agent cannot be made one:
/// the check constraint on the table holds it, and so does the absence of any
/// act on <see cref="Agent"/> that would set it.
/// </para>
/// <para>
/// Identities are never deleted (ADR 0013). There is no <c>DeletedAt</c> here
/// and never will be; a revoked token still names the identity everywhere it
/// ever acted.
/// </para>
/// </remarks>
public abstract class Identity
{
    public const int NameMaxLength = 100;

    protected Identity()
    {
        // EF Core materializes through this; every other route goes through the
        // derived types' Create.
    }

    protected Identity(Guid id, string name, bool administrator, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Administrator = administrator;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private init; }

    /// <summary>Which of the two this is; a fact of the type, not a column the type reads.</summary>
    public abstract IdentityKind Kind { get; }

    /// <summary>
    /// Unique across both kinds, case-insensitively, because the API and the
    /// CLI address identities by name and a name that could mean two things is
    /// no address. Changeable at any time; an agent's is assigned on creation
    /// (VISION 12).
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Whether this identity administers the instance — users, projects, and
    /// everything outside a single project's content. Held by users only.
    /// </summary>
    public bool Administrator { get; protected set; }

    public DateTimeOffset CreatedAt { get; private init; }

    public void Rename(string name) => Name = NormalizeName(name);

    /// <summary>
    /// The name as it would be stored. Public because uniqueness is asked about
    /// before it is written: whoever looks a name up has to ask about the string
    /// this would store rather than the one that was typed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is blank or longer than <see cref="NameMaxLength"/>.
    /// </exception>
    public static string NormalizeName(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("An identity has a name.", nameof(name));
        }

        return trimmed.Length > NameMaxLength
            ? throw new ArgumentException(
                $"A name is at most {NameMaxLength} characters.", nameof(name))
            : trimmed;
    }
}
