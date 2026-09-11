using Hostingaffe.Application.Acts;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What the import does to the cross-references a Markdown repository carries:
/// a relative <c>.md</c> path becomes the page it arrives as, and a path the
/// document does not account for is left exactly as it was (ADR 0007).
/// </summary>
public sealed class ImportLinkTests
{
    private static readonly IReadOnlyList<ImportPage> Document =
    [
        Page("caddy-in-front", "docs/decisions/2026-09-10-caddy-in-front.md"),
        Page("setup", "docs/setup/README.md"),
        Page("operations", "docs/operations/README.md"),
        Page("overview", "README.md"),
    ];

    private static readonly Dictionary<string, string> Sources = ImportLinks.Sources(Document);

    [Theory]
    // Out of the directory it sits in and into another.
    [InlineData(
        "docs/setup/README.md",
        "See [ADR](../decisions/2026-09-10-caddy-in-front.md).",
        "See [ADR](page:caddy-in-front).")]
    // Beside it, with the `./` a repository writes half the time.
    [InlineData("docs/setup/README.md", "[a](./../operations/README.md)", "[a](page:operations)")]
    // From a directory up to the root, and from the root down.
    [InlineData("docs/setup/README.md", "[up](../../README.md)", "[up](page:overview)")]
    [InlineData("README.md", "[down](docs/setup/README.md)", "[down](page:setup)")]
    // A path from the root, written with the leading slash that means it.
    [InlineData("docs/setup/README.md", "[root](/docs/operations/README.md)", "[root](page:operations)")]
    // A title survives, and so does the fragment: it says which part was meant.
    [InlineData("README.md", "[a](docs/setup/README.md \"Setup\")", "[a](page:setup \"Setup\")")]
    [InlineData("README.md", "[a](docs/setup/README.md#tokens)", "[a](page:setup#tokens)")]
    // The other spelling of a link, and the one an image uses.
    [InlineData("README.md", "[a][s]\n\n[s]: docs/setup/README.md", "[a][s]\n\n[s]: page:setup")]
    public void A_path_the_document_accounts_for_becomes_the_page_it_arrives_as(
        string from, string body, string expected)
    {
        var (rewritten, count) = ImportLinks.Rewrite(body, from, Sources);

        Assert.Equal(expected, rewritten);
        Assert.Equal(1, count);
    }

    [Theory]
    // Nothing in the document arrives under it.
    [InlineData("README.md", "[a](docs/security/README.md)")]
    // Out of the tree the document describes altogether.
    [InlineData("README.md", "[a](../other-repository/README.md)")]
    // Not Markdown, not relative, and not a document link at all.
    [InlineData("README.md", "[a](docs/setup/compose.yml)")]
    [InlineData("README.md", "[a](https://example.org/docs/setup/README.md)")]
    [InlineData("README.md", "[a](#a-heading-of-this-page)")]
    // Already an address of the record: the author said where it points.
    [InlineData("README.md", "[a](page:setup)")]
    public void Everything_else_is_left_exactly_as_it_was(string from, string body)
    {
        var (rewritten, count) = ImportLinks.Rewrite(body, from, Sources);

        Assert.Equal(body, rewritten);
        Assert.Equal(0, count);
    }

    [Fact]
    public void A_fenced_block_is_text_and_not_a_link()
    {
        const string body = """
            [a](docs/setup/README.md)

            ```sh
            cat [a](docs/setup/README.md)
            ```

            ~~~
            [b](docs/setup/README.md)
            ~~~

            [c](docs/setup/README.md)
            """;

        var (rewritten, count) = ImportLinks.Rewrite(body, "README.md", Sources);

        Assert.Equal(2, count);
        Assert.Contains("cat [a](docs/setup/README.md)", rewritten, StringComparison.Ordinal);
        Assert.Contains("[b](docs/setup/README.md)", rewritten, StringComparison.Ordinal);
        Assert.Contains("[c](page:setup)", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_of_a_line_is_rewritten()
    {
        var (rewritten, count) = ImportLinks.Rewrite(
            "[a](docs/setup/README.md) and [b](docs/operations/README.md) and [c](nowhere.md)",
            "README.md",
            Sources);

        Assert.Equal("[a](page:setup) and [b](page:operations) and [c](nowhere.md)", rewritten);
        Assert.Equal(2, count);
    }

    [Fact]
    public void A_page_that_does_not_say_where_it_came_from_is_in_no_map()
    {
        var sources = ImportLinks.Sources([Page("setup", null), Page("operations", "docs/operations/README.md")]);

        Assert.Equal(["docs/operations/README.md"], sources.Keys);
    }

    [Fact]
    public void A_body_without_a_link_to_rewrite_is_the_same_string()
    {
        const string body = "Nothing to see, and a `](` that is not a link.";

        var (rewritten, count) = ImportLinks.Rewrite(body, "README.md", Sources);

        Assert.Same(body, rewritten);
        Assert.Equal(0, count);
    }

    private static ImportPage Page(string slug, string? path) =>
        new(slug, slug, null, null, null, path);
}
