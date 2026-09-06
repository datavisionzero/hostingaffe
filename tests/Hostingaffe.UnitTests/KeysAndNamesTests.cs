using Hostingaffe.Domain.Projects;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The two patterns docs/storage.md gives: the project key that prefixes every
/// key in the project, and the label name that is also the label group's.
/// </summary>
public sealed class KeysAndNamesTests
{
    [Theory]
    [InlineData("PLAN")]
    [InlineData("A1")]
    [InlineData("ABCDEFGHIJ")]
    public void A_project_key_is_upper_case_two_to_ten_characters(string key) =>
        Assert.Equal(key, ProjectKey.Normalize(key));

    [Theory]
    [InlineData("plan", "lower case")]
    [InlineData("P", "one character")]
    [InlineData("ABCDEFGHIJK", "eleven characters")]
    [InlineData("1PLAN", "starts with a digit")]
    [InlineData("PL-AN", "the hyphen is what separates it from the number")]
    [InlineData("", "empty")]
    public void A_project_key_is_refused_when_it_is(string key, string reason)
    {
        var refusal = Assert.Throws<ArgumentException>(() => ProjectKey.Normalize(key));
        Assert.Equal("key", refusal.ParamName);
        Assert.NotEmpty(reason);
    }

    [Theory]
    [InlineData("bug")]
    [InlineData("area:infra")]
    [InlineData("cut-1")]
    [InlineData("repo/hostingaffe")]
    public void A_label_name_is_lower_case(string name) =>
        Assert.Equal(name, LabelName.Normalize(name));

    [Theory]
    [InlineData("Bug")]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("")]
    public void A_label_name_is_refused_when_it_is_not(string name) =>
        Assert.Throws<ArgumentException>(() => LabelName.Normalize(name));

    [Fact]
    public void A_label_group_follows_the_same_pattern()
    {
        var refusal = Assert.Throws<ArgumentException>(
            () => Label.Create(Guid.NewGuid(), "bug", "Kind", null, DateTimeOffset.UnixEpoch));

        Assert.Equal("group", refusal.ParamName);
    }
}
