using Hostingaffe.Domain;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The shapes every editable field shares, tested once rather than once per
/// entity that uses them (<c>Fields</c>).
/// </summary>
public sealed class FieldsTests
{
    [Theory]
    [InlineData("https://caddyserver.com")]
    [InlineData("http://example.test/path")]
    public void A_url_is_an_absolute_http_address(string url) => Assert.Equal(url, Fields.Url(url, "homepage"));

    [Theory]
    [InlineData("caddyserver.com")]
    [InlineData("ftp://example.test")]
    [InlineData("/relative")]
    [InlineData("https://")]
    public void Anything_else_is_not_a_url(string url) =>
        Assert.Throws<ArgumentException>(() => Fields.Url(url, "homepage"));

    [Fact]
    public void A_line_is_one_line_and_no_longer_than_it_may_be()
    {
        Assert.Equal("fsn1-dc14", Fields.Line("fsn1-dc14", 200, "A location"));
        Assert.Throws<ArgumentException>(() => Fields.Line("one\ntwo", 200, "A location"));
        Assert.Throws<ArgumentException>(() => Fields.Line(new string('x', 201), 200, "A location"));
    }

    [Fact]
    public void A_note_is_trimmed_and_an_empty_one_is_no_note_at_all()
    {
        Assert.Equal("dist-upgrade", Fields.Note("  dist-upgrade  "));
        Assert.Null(Fields.Note(null));
        Assert.Null(Fields.Note(""));
        Assert.Null(Fields.Note("   "));
    }

    [Fact]
    public void A_note_is_one_line_and_says_why_rather_than_telling_the_story()
    {
        Assert.Throws<ArgumentException>(() => Fields.Note("why\nand how"));
        Assert.Throws<ArgumentException>(() => Fields.Note(new string('x', Fields.NoteMaxLength + 1)));
        Assert.Equal(new string('x', Fields.NoteMaxLength), Fields.Note(new string('x', Fields.NoteMaxLength)));
    }
}
