using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.UnitTests;

/// <summary>The rules of VISION 11 on the Domain type, without a database.</summary>
public sealed class ClaimRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    private static readonly Guid Agent = Guid.NewGuid();

    private static readonly Guid OtherAgent = Guid.NewGuid();

    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void An_agents_claim_expires_after_the_deadline_and_a_users_never()
    {
        var byAgent = Fresh();
        byAgent.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);
        Assert.Equal(IssueStatus.InProgress, byAgent.Status);
        Assert.Equal(Now + FourHours, byAgent.Claim!.ExpiresAt);
        Assert.False(byAgent.Claim.ExpiredAt(Now.AddHours(3.99)));
        Assert.True(byAgent.Claim.ExpiredAt(Now.AddHours(4)));

        var byUser = Fresh();
        byUser.ClaimFor(User, IdentityKind.User, false, Now, FourHours);
        Assert.Null(byUser.Claim!.ExpiresAt);
        Assert.False(byUser.Claim.ExpiredAt(Now.AddYears(1)));
    }

    [Fact]
    public void The_holder_extends_and_nobody_else_does()
    {
        var issue = Fresh();
        issue.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);

        var again = issue.ClaimFor(Agent, IdentityKind.Agent, false, Now.AddHours(1), FourHours);
        Assert.Equal(ClaimOutcomeKind.Extended, again.Kind);
        Assert.Equal(Now, issue.Claim!.ClaimedAt);
        Assert.Equal(Now.AddHours(1), issue.Claim.ExtendedAt);
        Assert.Equal(Now.AddHours(5), issue.Claim.ExpiresAt);

        issue.ExtendClaimIfHeldBy(User, IdentityKind.User, Now.AddHours(2), FourHours);
        Assert.Equal(Now.AddHours(5), issue.Claim.ExpiresAt);

        issue.ExtendClaimIfHeldBy(Agent, IdentityKind.Agent, Now.AddHours(2), FourHours);
        Assert.Equal(Now.AddHours(6), issue.Claim.ExpiresAt);
    }

    [Fact]
    public void Somebody_elses_unexpired_claim_is_held_and_forced_only_with_force()
    {
        var issue = Fresh();
        issue.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);

        var held = Assert.Throws<Refusal>(() => issue.ClaimFor(OtherAgent, IdentityKind.Agent, false, Now.AddHours(1), FourHours));
        Assert.Equal(RefusalCode.ClaimHeld, held.Code);
        Assert.Equal(Agent, held.Extensions["holder"]);

        var forced = issue.ClaimFor(OtherAgent, IdentityKind.Agent, true, Now.AddHours(1), FourHours);
        Assert.Equal(ClaimOutcomeKind.Forced, forced.Kind);
        Assert.Equal(Agent, forced.PreviousHolder);
        Assert.Equal(OtherAgent, issue.Claim!.HolderId);
    }

    [Fact]
    public void A_users_claim_is_protected_against_agents_and_not_against_users()
    {
        var issue = Fresh();
        issue.ClaimFor(User, IdentityKind.User, false, Now, FourHours);

        var refused = Assert.Throws<Refusal>(() => issue.ClaimFor(Agent, IdentityKind.Agent, true, Now.AddDays(3), FourHours));
        Assert.Equal(RefusalCode.ClaimProtected, refused.Code);

        var otherUser = Guid.NewGuid();
        Assert.Equal(ClaimOutcomeKind.Forced, issue.ClaimFor(otherUser, IdentityKind.User, true, Now.AddDays(3), FourHours).Kind);
    }

    [Fact]
    public void An_expired_claim_is_taken_and_the_successor_knows_whose_it_was()
    {
        var issue = Fresh();
        issue.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);

        var taken = issue.ClaimFor(OtherAgent, IdentityKind.Agent, false, Now.AddHours(5), FourHours);
        Assert.Equal(ClaimOutcomeKind.TakenAfterExpiry, taken.Kind);
        Assert.Equal(Agent, taken.PreviousHolder);
    }

    [Fact]
    public void Review_and_closed_issues_are_not_claimed()
    {
        var review = Fresh();
        typeof(Issue).GetProperty(nameof(Issue.Status))!.SetValue(review, IssueStatus.Review);
        Assert.Equal(RefusalCode.Transition, Assert.Throws<Refusal>(() => review.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours)).Code);

        var done = Fresh();
        typeof(Issue).GetProperty(nameof(Issue.Status))!.SetValue(done, IssueStatus.Done);
        Assert.Equal(RefusalCode.Transition, Assert.Throws<Refusal>(() => done.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours)).Code);
    }

    [Fact]
    public void Releasing_lands_in_todo_and_needs_a_holder()
    {
        var issue = Fresh();
        Assert.Equal(RefusalCode.Transition, Assert.Throws<Refusal>(() => issue.Release(Now)).Code);

        issue.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);
        Assert.Equal(Agent, issue.Release(Now.AddHours(1)));
        Assert.Equal(IssueStatus.Todo, issue.Status);
        Assert.Null(issue.Claim);

        // A claim that has lapsed is nobody's to release.
        issue.ClaimFor(Agent, IdentityKind.Agent, false, Now, FourHours);
        Assert.Equal(RefusalCode.Transition, Assert.Throws<Refusal>(() => issue.Release(Now.AddHours(9))).Code);
    }

    private static Issue Fresh() => Issue.Create(Guid.NewGuid(), 1, "An issue", User, Now);
}
