using Hostingaffe.Domain.Projects;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The project key that prefixes everything in the project
/// (docs/storage.md).
/// </summary>
public sealed class ProjectKeyTests
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
}
