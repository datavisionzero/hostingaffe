namespace Hostingaffe.Domain;

/// <summary>
/// How one thing of the record names another inside a Markdown body: an
/// ordinary link whose target is a scheme and an address — <c>page:backup-restore</c>,
/// <c>machine:ex44</c>, <c>software:caddy</c>, <c>installation:app-1</c>
/// (ADR 0007).
/// </summary>
/// <remarks>
/// <para>
/// The scheme carries the type because the address does not: a key is unique
/// per entity type and not across the instance, so the machine <c>caddy</c> and
/// the software <c>caddy</c> coexist, and <c>CONTEXT.md</c> says that a key
/// standing alone in a Markdown link carries its type.
/// </para>
/// <para>
/// Nothing here validates a body. The instance stores Markdown and does not
/// parse it; what resolves these targets is the reader — the web application
/// navigates them, and <c>ha page check</c> says which of them point at
/// nothing. This type is what the one place that writes such a link — the
/// import — spells it with.
/// </para>
/// </remarks>
public static class Link
{
    public const string PageScheme = "page";
    public const string MachineScheme = "machine";
    public const string SoftwareScheme = "software";
    public const string InstallationScheme = "installation";

    /// <summary>What a link to the page <paramref name="slug"/> has as its target.</summary>
    public static string ToPage(string slug) => $"{PageScheme}:{slug}";
}
