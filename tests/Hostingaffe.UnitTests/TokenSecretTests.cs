using System.Text.RegularExpressions;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class TokenSecretTests
{
    [Fact]
    public void A_generated_secret_is_the_prefix_and_43_characters_of_the_alphabet()
    {
        var secret = TokenSecret.Generate();

        Assert.Matches("^ha_[A-Za-z0-9]{43}$", secret);
        Assert.Equal("ha_" + secret[3..8], TokenSecret.PrefixOf(secret));
        Assert.Equal(32, TokenSecret.HashOf(secret).Length);
    }

    [Fact]
    public void Two_generated_secrets_differ()
    {
        Assert.NotEqual(TokenSecret.Generate(), TokenSecret.Generate());
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void A_secret_is_32_to_200_characters(int length, bool acceptable)
    {
        Assert.Equal(acceptable, TokenSecret.IsAcceptable(new string('x', length)));
    }

    [Fact]
    public void A_token_issued_from_a_secret_keeps_the_prefix_and_the_hash_and_not_the_secret()
    {
        var user = User.Create("maintainer", administrator: false, DateTimeOffset.UnixEpoch);
        var secret = TokenSecret.Generate();

        var token = Token.Issue(user, secret, DateTimeOffset.UnixEpoch);

        Assert.Equal(secret[..8], token.Prefix);
        Assert.Equal(TokenSecret.HashOf(secret), token.SecretHash);
        Assert.DoesNotContain(token.GetType().GetProperties(), p => p.GetValue(token) is string s && s == secret);
    }

    [Fact]
    public void A_too_short_secret_is_refused_at_issue()
    {
        var user = User.Create("maintainer", administrator: false, DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentException>(() => Token.Issue(user, "short", DateTimeOffset.UnixEpoch));
    }
}
