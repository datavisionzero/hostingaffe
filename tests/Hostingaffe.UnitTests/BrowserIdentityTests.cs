using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class BrowserIdentityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-04T10:00:00Z");

    [Fact]
    public void Email_is_trimmed_unicode_normalized_and_lowered_for_comparison()
    {
        var user = User.Create("maintainer", "  MAINTAINER@Example.Test  ", false, Now);
        Assert.Equal("MAINTAINER@Example.Test", user.Email);
        Assert.Equal("maintainer@example.test", user.NormalizedEmail);
    }

    [Fact]
    public void Invitation_lives_seven_days_and_recovery_one_hour()
    {
        var invitation = OneTimeSecret.Issue(Guid.NewGuid(), OneTimeSecretPurpose.Invitation, Now);
        var recovery = OneTimeSecret.Issue(Guid.NewGuid(), OneTimeSecretPurpose.PasswordRecovery, Now);
        Assert.Equal(Now.AddDays(7), invitation.Record.ExpiresAt);
        Assert.Equal(Now.AddHours(1), recovery.Record.ExpiresAt);
        Assert.Equal(invitation.Record.SecretHash, OneTimeSecret.Hash(invitation.Secret));
    }

    [Fact]
    public void Session_has_idle_and_absolute_boundaries_and_is_touched_sparingly()
    {
        var issued = BrowserSession.Create(Guid.NewGuid(), Now);
        Assert.False(issued.Session.Touch(Now.AddMinutes(4)));
        Assert.True(issued.Session.Touch(Now.AddMinutes(5)));
        Assert.False(issued.Session.IsValid(Now.AddDays(30)));
        Assert.Equal(issued.Session.SecretHash, BrowserSession.Hash(issued.Secret));
    }
}
