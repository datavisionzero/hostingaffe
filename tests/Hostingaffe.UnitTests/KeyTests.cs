using Hostingaffe.Domain;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The shape of a key (<c>CONTEXT.md</c>, Key): what an operator may choose,
/// and what the instance refuses before it ever reaches a table.
/// </summary>
public sealed class KeyTests
{
    [Theory]
    [InlineData("caddy")]
    [InlineData("ex44")]
    [InlineData("docker-prod-01")]
    [InlineData("logaffe-prod")]
    [InlineData("a")]
    public void The_keys_of_the_vision_are_keys(string key) => Assert.Equal(key, Key.Normalize(key));

    [Theory]
    [InlineData("")]
    [InlineData("Caddy")]
    [InlineData("caddy_prod")]
    [InlineData("caddy prod")]
    [InlineData("-caddy")]
    [InlineData("caddy-")]
    [InlineData("caddy--prod")]
    [InlineData("caddy/backup")]
    [InlineData("caddy.prod")]
    public void What_is_not_a_key_is_refused(string key) =>
        Assert.Throws<ArgumentException>(() => Key.Normalize(key));

    [Fact]
    public void A_key_is_short()
    {
        Assert.True(Key.IsValid(new string('a', Key.MaxLength)));
        Assert.False(Key.IsValid(new string('a', Key.MaxLength + 1)));
    }

    [Fact]
    public void Surrounding_space_is_not_part_of_it() => Assert.Equal("caddy", Key.Normalize("  caddy  "));
}
