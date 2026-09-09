using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class UserCodeTests
{
    [Fact]
    public void A_code_is_eight_consonants_and_reads_in_two_groups()
    {
        var code = UserCode.Issue();

        Assert.Equal(UserCode.Length, code.Length);
        Assert.All(code, character => Assert.Contains(character, UserCode.Alphabet));
        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", UserCode.ForReading(code));
    }

    /// <summary>
    /// No vowel and no digit, which is what keeps a code from coming out as a
    /// word and what removes every pair a person retypes — <c>0</c> and
    /// <c>O</c>, <c>1</c> and <c>I</c>, <c>5</c> and <c>S</c>, <c>2</c> and
    /// <c>Z</c>.
    /// </summary>
    [Fact]
    public void The_alphabet_carries_no_vowel_and_no_digit()
    {
        Assert.DoesNotContain(UserCode.Alphabet, character => "AEIOU".Contains(character) || char.IsDigit(character));
        Assert.Equal(20, UserCode.Alphabet.Length);
    }

    [Theory]
    [InlineData("bcdf-ghjk", "BCDFGHJK")]
    [InlineData(" BCDF GHJK ", "BCDFGHJK")]
    [InlineData("BCDFGHJK", "BCDFGHJK")]
    public void Case_spaces_and_dashes_are_how_a_code_was_shown_and_not_what_it_is(string typed, string stored)
    {
        Assert.Equal(stored, UserCode.Normalize(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BCDFGHJ")]
    [InlineData("BCDFGHJKL")]
    [InlineData("BCDFGHJ0")]
    [InlineData("BCDFGHJA")]
    public void What_is_not_a_code_normalizes_to_nothing_rather_than_to_a_lookup(string? typed)
    {
        Assert.Equal(string.Empty, UserCode.Normalize(typed));
    }
}

public sealed class DeviceLoginTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_begun_login_keeps_the_hash_of_the_device_code_and_never_the_code()
    {
        var (login, deviceCode, userCode) = DeviceLogin.Begin(Now);

        Assert.Equal(DeviceCode.Hash(deviceCode), login.DeviceCodeHash);
        Assert.Equal(userCode, login.UserCode);
        Assert.Equal(Now + DeviceLogin.Lifetime, login.ExpiresAt);
        Assert.Equal(DeviceLoginState.Pending, login.StateAt(Now));
        Assert.DoesNotContain(
            login.GetType().GetProperties(),
            property => property.GetValue(login) is string held && held == deviceCode);
    }

    [Fact]
    public void An_approved_login_hands_its_token_over_once()
    {
        var (login, _, _) = DeviceLogin.Begin(Now);
        var user = Guid.CreateVersion7();

        Assert.True(login.ApproveBy(user, Now));
        Assert.Equal(DeviceLoginState.Approved, login.StateAt(Now));

        var first = Guid.CreateVersion7();
        Assert.True(login.RedeemTo(first, Now));
        Assert.Equal(DeviceLoginState.Redeemed, login.StateAt(Now));

        Assert.False(login.RedeemTo(Guid.CreateVersion7(), Now));
        Assert.Equal(first, login.IssuedTokenId);
    }

    [Fact]
    public void A_login_nobody_approved_in_time_is_expired_and_not_approvable()
    {
        var (login, _, _) = DeviceLogin.Begin(Now);
        var late = Now + DeviceLogin.Lifetime;

        Assert.Equal(DeviceLoginState.Expired, login.StateAt(late));
        Assert.False(login.ApproveBy(Guid.CreateVersion7(), late));
    }

    /// <summary>
    /// A device code left behind in a CI log is worth nothing an hour later:
    /// an approval nobody collected in time reads as expired rather than as
    /// still redeemable.
    /// </summary>
    [Fact]
    public void An_approval_nobody_collected_in_time_expires_with_the_login()
    {
        var (login, _, _) = DeviceLogin.Begin(Now);
        login.ApproveBy(Guid.CreateVersion7(), Now);

        var late = Now + DeviceLogin.Lifetime;

        Assert.Equal(DeviceLoginState.Expired, login.StateAt(late));
        Assert.False(login.RedeemTo(Guid.CreateVersion7(), late));
    }

    [Fact]
    public void A_refused_login_stays_refused_and_a_redeemed_one_stays_redeemed()
    {
        var (refused, _, _) = DeviceLogin.Begin(Now);
        Assert.True(refused.Deny(Now));
        Assert.False(refused.ApproveBy(Guid.CreateVersion7(), Now));
        Assert.Equal(DeviceLoginState.Denied, refused.StateAt(Now));
        Assert.Equal(DeviceLoginState.Denied, refused.StateAt(Now + DeviceLogin.Lifetime));

        var (redeemed, _, _) = DeviceLogin.Begin(Now);
        redeemed.ApproveBy(Guid.CreateVersion7(), Now);
        redeemed.RedeemTo(Guid.CreateVersion7(), Now);
        Assert.Equal(DeviceLoginState.Redeemed, redeemed.StateAt(Now + DeviceLogin.Lifetime));
    }

    [Fact]
    public void Two_logins_share_neither_code()
    {
        var (_, firstDevice, firstUser) = DeviceLogin.Begin(Now);
        var (_, secondDevice, secondUser) = DeviceLogin.Begin(Now);

        Assert.NotEqual(firstDevice, secondDevice);
        Assert.NotEqual(firstUser, secondUser);
    }
}
