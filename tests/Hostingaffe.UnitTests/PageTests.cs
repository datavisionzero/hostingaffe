using Hostingaffe.Domain.Pages;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The rules the page holds by itself: what a slug may look like, that a title
/// is one line, and that every edit moves the version and names who made it
/// (<c>CONTEXT.md</c>, Page; ADR 0021).
/// </summary>
public sealed class PageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Project = Guid.CreateVersion7();

    private static readonly Guid Author = Guid.CreateVersion7();

    [Theory]
    [InlineData("architecture")]
    [InlineData("betriebshandbuch")]
    [InlineData("adr-0021")]
    [InlineData("a")]
    [InlineData("7")]
    [InlineData("one-two-three")]
    public void A_slug_is_lower_case_words_joined_by_single_hyphens(string slug) =>
        Assert.Equal(slug, Slug.Normalize(slug));

    [Theory]
    [InlineData("")]
    [InlineData("-architecture")]
    [InlineData("architecture-")]
    [InlineData("two--hyphens")]
    [InlineData("Architecture")]
    [InlineData("with space")]
    [InlineData("with_underscore")]
    [InlineData("with/slash")]
    [InlineData("with.dot")]
    [InlineData("umlaut-ä")]
    public void Everything_else_is_not_a_slug(string slug) =>
        Assert.Throws<ArgumentException>(() => Slug.Normalize(slug));

    [Fact]
    public void A_slug_has_a_ceiling()
    {
        Assert.Equal(Slug.MaxLength, Slug.Normalize(new string('a', Slug.MaxLength)).Length);
        Assert.Throws<ArgumentException>(() => Slug.Normalize(new string('a', Slug.MaxLength + 1)));
    }

    [Fact]
    public void Surrounding_space_is_not_part_of_the_address() =>
        Assert.Equal("architecture", Slug.Normalize("  architecture  "));

    [Fact]
    public void A_new_page_is_its_author_in_both_places()
    {
        var page = Page.Create(Project, "architecture", "Architecture", null, Author, Now);

        Assert.Equal("architecture", page.Slug);
        Assert.Equal("Architecture", page.Title);
        Assert.Equal(string.Empty, page.Body);
        Assert.Equal(Author, page.CreatedBy);
        Assert.Equal(Author, page.UpdatedBy);
        Assert.Equal(Now, page.CreatedAt);
        Assert.Equal(Now, page.UpdatedAt);
        Assert.False(page.Deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    public void A_title_is_one_line_and_not_blank(string title) =>
        Assert.Throws<ArgumentException>(() => Page.Create(Project, "architecture", title, null, Author, Now));

    [Fact]
    public void A_title_has_a_ceiling() =>
        Assert.Throws<ArgumentException>(() =>
            Page.Create(Project, "architecture", new string('a', Page.TitleMaxLength + 1), null, Author, Now));

    [Fact]
    public void Every_edit_moves_the_version_and_says_who()
    {
        var page = Page.Create(Project, "architecture", "Architecture", "# Old", Author, Now);
        var editor = Guid.CreateVersion7();
        var later = Now.AddHours(1);

        page.Rewrite("# New", editor, later);

        Assert.Equal("# New", page.Body);
        Assert.Equal(editor, page.UpdatedBy);
        Assert.Equal(later, page.UpdatedAt);
        Assert.Equal(Author, page.CreatedBy);
        Assert.Equal(Now, page.CreatedAt);
    }

    [Fact]
    public void Rewriting_with_nothing_empties_the_document()
    {
        var page = Page.Create(Project, "architecture", "Architecture", "# Old", Author, Now);

        page.Rewrite(null, Author, Now.AddHours(1));

        Assert.Equal(string.Empty, page.Body);
    }

    [Fact]
    public void Renaming_changes_the_address_and_nothing_else()
    {
        var page = Page.Create(Project, "architecture", "Architecture", "# The body", Author, Now);

        page.Rename("betriebshandbuch", Author, Now.AddHours(1));

        Assert.Equal("betriebshandbuch", page.Slug);
        Assert.Equal("Architecture", page.Title);
        Assert.Equal("# The body", page.Body);
        Assert.Equal(Now.AddHours(1), page.UpdatedAt);
    }

    [Fact]
    public void A_rename_to_something_that_is_not_a_slug_is_refused()
    {
        var page = Page.Create(Project, "architecture", "Architecture", null, Author, Now);

        Assert.Throws<ArgumentException>(() => page.Rename("Not A Slug", Author, Now.AddHours(1)));
        Assert.Equal("architecture", page.Slug);
    }

    [Fact]
    public void Deleting_is_soft_and_restoring_undoes_exactly_it()
    {
        var page = Page.Create(Project, "architecture", "Architecture", "# The body", Author, Now);
        var deleter = Guid.CreateVersion7();

        page.Delete(deleter, Now.AddHours(1));

        Assert.True(page.Deleted);
        Assert.Equal(deleter, page.DeletedBy);
        Assert.Equal(Now.AddHours(1), page.DeletedAt);

        // The version does not move: a deletion is not an edit of the document,
        // and a guarded write held against the read before it still applies.
        Assert.Equal(Now, page.UpdatedAt);

        page.Restore();

        Assert.False(page.Deleted);
        Assert.Null(page.DeletedBy);
        Assert.Null(page.DeletedAt);
        Assert.Equal("architecture", page.Slug);
    }

    [Fact]
    public void Deleting_twice_keeps_the_first_deletion()
    {
        var page = Page.Create(Project, "architecture", "Architecture", null, Author, Now);
        var first = Guid.CreateVersion7();

        page.Delete(first, Now.AddHours(1));
        page.Delete(Guid.CreateVersion7(), Now.AddHours(2));

        Assert.Equal(first, page.DeletedBy);
        Assert.Equal(Now.AddHours(1), page.DeletedAt);
    }
}
