using System.Text.RegularExpressions;

namespace Hostingaffe.Domain.Installations;

/// <summary>
/// One secret an installation needs: the <em>name</em> somebody has to set, and
/// the file on the machine its value lies in (<c>CONTEXT.md</c>, Installation;
/// ADR 0011).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Never the value.</strong> That rule is older than this type
/// (VISION 7) and is what the name's shape enforces: a name is one word, so a
/// line of an <c>.env</c> file cannot be pasted in here whole.
/// </para>
/// <para>
/// The place is what was missing while a secret was a word. It is the fact an
/// operator needs first — which file a restore has to bring back, which one
/// must be <c>0600</c>, which one must never reach a repository — and as prose
/// in a description it was a fact nothing could search.
/// </para>
/// <para>
/// An object rather than the string <c>POSTGRES_PASSWORD@/srv/logaffe/.env</c>,
/// for the reason a port is one: that spelling is a <em>rendering</em> — what a
/// person types, and what a history row carries — while as a field it would be
/// a grammar of its own that a generated client can read nothing out of.
/// </para>
/// </remarks>
public sealed partial class Secret
{
    /// <summary>What the name of a secret fits in.</summary>
    public const int NameMaxLength = 200;

    /// <summary>
    /// The shape of a secret's <em>name</em>: a word, not a sentence and not a
    /// value. Whitespace, <c>=</c> and <c>@</c> fall outside it — the first two
    /// because a value is not recorded here, the last because it is what the
    /// spelling puts between the name and the place.
    /// </summary>
    public const string NamePattern = "^[A-Za-z_][A-Za-z0-9_./-]*$";

    private Secret()
    {
        // EF Core materializes through this; every other route goes through Of.
    }

    private Secret(string name, string? path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>The name it is set under — <c>POSTGRES_PASSWORD</c>.</summary>
    public string Name { get; private init; } = null!;

    /// <summary>
    /// The file its value lies in on the machine —
    /// <c>/opt/compose/logaffe/.env.runtime</c>, absolute. Optional: an
    /// installation may know that it needs a secret before anybody has decided
    /// where it goes.
    /// </summary>
    public string? Path { get; private init; }

    /// <exception cref="ArgumentException">It is not the shape of a name, or the path is not one.</exception>
    public static Secret Of(string? name, string? path)
    {
        var named = name?.Trim() ?? string.Empty;
        Fields.Line(named, NameMaxLength, "A secret name");

        if (!SecretName().IsMatch(named))
        {
            throw new ArgumentException(
                $"A secret is named, never given: a name is one word ({NamePattern}), "
                + "and the value belongs nowhere near here.",
                nameof(name));
        }

        // Nothing said is nothing recorded: a secret whose place is unknown is
        // still a secret the installation needs.
        var lies = path?.Trim() ?? string.Empty;

        return new Secret(
            named,
            lies.Length == 0
                ? null
                : Fields.Absolute(lies, "A secret lies in a file on the machine, from the root: /opt/compose/logaffe/.env.runtime."));
    }

    /// <summary>
    /// The spelling a person writes and reads — <c>POSTGRES_PASSWORD</c> where
    /// nobody has said where it lies, and
    /// <c>POSTGRES_PASSWORD@/opt/compose/logaffe/.env.runtime</c> where somebody
    /// has. It is what a history row carries, for the same reason a port's is.
    /// </summary>
    public override string ToString() => Path is null ? Name : $"{Name}@{Path}";

    [GeneratedRegex(NamePattern)]
    private static partial Regex SecretName();
}
