namespace Hostingaffe.Domain;

/// <summary>
/// The two things something else in the record can hang on
/// (<c>CONTEXT.md</c>, File and Page).
/// </summary>
public enum AnchorKind
{
    Machine,
    Installation,
}

/// <summary>
/// A machine or an installation, named by both the kind and the key: a file's
/// <c>owner</c>, a page's <c>attached_to</c>.
/// </summary>
/// <remarks>
/// <para>
/// The kind is part of the answer and not decoration. A key is unique per
/// entity type and not across them (<c>CONTEXT.md</c>, Key), so a machine
/// <c>caddy</c> and an installation <c>caddy</c> both exist and usually will —
/// something that carried only the key would name two things.
/// </para>
/// <para>
/// This type is the shape the two fields share; the field names stay the
/// glossary's. A file is <em>owned</em>, a page is <em>attached</em>, and
/// nothing here renames either.
/// </para>
/// </remarks>
public sealed record Anchor(AnchorKind Kind, Guid Id, string Key)
{
    public override string ToString() => $"{Spelling.Of(Kind)} {Key}";
}
