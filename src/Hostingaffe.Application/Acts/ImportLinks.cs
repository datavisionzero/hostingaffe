using System.Text.RegularExpressions;
using Hostingaffe.Domain;

namespace Hostingaffe.Application.Acts;

/// <summary>
/// What the import does to the cross-references a Markdown repository is full
/// of: a relative <c>.md</c> path in a body becomes <c>page:slug</c>, the slug
/// of the page that arrives in the same document under that path (ADR 0007).
/// </summary>
/// <remarks>
/// <para>
/// The import is the only place that can do this without guessing. It holds
/// every page at once and knows where each of them came from, so the mapping
/// from a path to an address is read rather than inferred; afterwards the path
/// is gone and nothing could reconstruct it. A page that says where it came
/// from is what buys this — <c>path</c> is read for exactly this and is never
/// stored.
/// </para>
/// <para>
/// <strong>A path no page in the document claims is left alone.</strong> It
/// stays the text it was, which the renderer shows without a link, because a
/// dead link the reader can see beats an address the import invented.
/// </para>
/// </remarks>
public static partial class ImportLinks
{
    /// <summary>
    /// The target of an inline link — what stands between <c>](</c> and the
    /// closing parenthesis, before the optional title. A destination in angle
    /// brackets is left alone: it is the spelling for a path with spaces in it,
    /// and a repository that has one is not what this is for.
    /// </summary>
    [GeneratedRegex(@"(?<=\]\()(?<target>[^\s()<>]+)(?=(?:[ \t]+(?:""[^""]*""|'[^']*'))?\))")]
    private static partial Regex Inline();

    /// <summary>A link reference definition: <c>[label]: target</c>, at the start of its line.</summary>
    [GeneratedRegex(@"^(?<head>[ ]{0,3}\[[^\]]+\]:[ \t]*)(?<target>[^\s]+)")]
    private static partial Regex Definition();

    /// <summary>A fence opening or closing a code block, which nothing inside is rewritten in.</summary>
    [GeneratedRegex(@"^[ ]{0,3}(?<fence>`{3,}|~{3,})")]
    private static partial Regex Fence();

    /// <summary>
    /// Where each page of the document came from, mapped to the slug it arrives
    /// under. A page without a <c>path</c> is in no map; two pages under one
    /// path is the document contradicting itself, and the last one wins the way
    /// a later line of a file does — the slugs themselves are checked for
    /// collisions separately, which is the mistake that actually happens.
    /// </summary>
    public static Dictionary<string, string> Sources(IReadOnlyList<ImportPage> pages)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            if (Normalize(page.Path) is { Length: > 0 } path && page.Slug?.Trim() is { Length: > 0 } slug)
            {
                sources[path] = slug;
            }
        }

        return sources;
    }

    /// <summary>
    /// <paramref name="body"/> with every relative <c>.md</c> link that the
    /// document accounts for turned into the page it became, and the number of
    /// links that changed.
    /// </summary>
    /// <param name="from">The path the body itself came from; a relative link is resolved against its directory.</param>
    public static (string? Body, int Rewritten) Rewrite(
        string? body, string? from, IReadOnlyDictionary<string, string> sources)
    {
        if (string.IsNullOrEmpty(body) || sources.Count == 0)
        {
            return (body, 0);
        }

        var here = Directory(Normalize(from));
        var rewritten = 0;

        string Target(Match match)
        {
            var target = match.Groups["target"].Value;

            if (Resolve(target, here) is not { } path || !sources.TryGetValue(path, out var slug))
            {
                return match.Value;
            }

            rewritten++;
            var fragment = target.IndexOf('#', StringComparison.Ordinal);
            var head = match.Groups["head"];

            // The fragment is kept: it says which part of the page was meant,
            // and the address it hangs off is right either way.
            return (head.Success ? head.Value : string.Empty)
                + Link.ToPage(slug)
                + (fragment < 0 ? string.Empty : target[fragment..]);
        }

        var lines = body.Split('\n');
        var fence = (string?)null;

        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence().Match(lines[i]) is { Success: true } opened)
            {
                var said = opened.Groups["fence"].Value;
                fence = fence is null ? said : fence[0] == said[0] && said.Length >= fence.Length ? null : fence;
                continue;
            }

            if (fence is not null)
            {
                continue;
            }

            lines[i] = Definition().Replace(Inline().Replace(lines[i], Target), Target);
        }

        return (rewritten == 0 ? body : string.Join('\n', lines), rewritten);
    }

    /// <summary>
    /// The document path a link target names, or nothing where it is not a
    /// relative path to a Markdown file — a foreign URL, a link of the record's
    /// own schemes, an anchor into this very page, or a file that is not
    /// Markdown.
    /// </summary>
    private static string? Resolve(string target, string here)
    {
        if (target.Length == 0 || target[0] == '#')
        {
            return null;
        }

        // A scheme means the author already said where this points, whether it
        // is `https:` or one of the record's own.
        var colon = target.IndexOf(':', StringComparison.Ordinal);
        var slash = target.IndexOf('/', StringComparison.Ordinal);
        if (colon > 0 && (slash < 0 || colon < slash))
        {
            return null;
        }

        var fragment = target.IndexOf('#', StringComparison.Ordinal);
        var path = fragment < 0 ? target : target[..fragment];

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // A leading slash is the repository's root, which is where the paths in
        // the document are measured from anyway.
        var walked = new List<string>();
        if (path[0] != '/')
        {
            walked.AddRange(here.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case "..":
                    if (walked.Count == 0)
                    {
                        // Out of the tree the document describes, and therefore
                        // out of anything it could be mapped onto.
                        return null;
                    }

                    walked.RemoveAt(walked.Count - 1);
                    break;
                default:
                    walked.Add(segment);
                    break;
            }
        }

        return string.Join('/', walked);
    }

    /// <summary>A path as the map spells it: forward slashes, no <c>./</c>, no leading slash.</summary>
    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var walked = new List<string>();

        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    break;
                case ".." when walked.Count > 0:
                    walked.RemoveAt(walked.Count - 1);
                    break;
                case "..":
                    break;
                default:
                    walked.Add(segment);
                    break;
            }
        }

        return string.Join('/', walked);
    }

    /// <summary>The directory a path sits in, which a relative link is measured from.</summary>
    private static string Directory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}
