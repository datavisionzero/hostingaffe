using Hostingaffe.Domain.Deployments;

namespace Hostingaffe.UnitTests;

/// <summary>
/// What a deployment holds itself to: it says which version ran, it has no
/// status, and the four fields a correction may touch are the only four.
/// </summary>
public sealed class DeploymentTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static readonly Guid Installation = Guid.CreateVersion7();

    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static Deployment A(string version = "1.4.0") =>
        Deployment.Record(Installation, 1, version, Now, Actor, Now);

    [Fact]
    public void A_deployment_is_a_version_that_ran()
    {
        var deployment = A();

        Assert.Equal("1.4.0", deployment.Version);
        Assert.Equal(1, deployment.Number);
        Assert.Equal(Now, deployment.At);
        Assert.Equal(string.Empty, deployment.Note);
    }

    [Fact]
    public void It_says_which_version() =>
        Assert.Throws<ArgumentException>(() => Deployment.Record(Installation, 1, "  ", Now, Actor, Now));

    [Fact]
    public void There_is_no_status_to_set()
    {
        Assert.Null(typeof(Deployment).GetProperty("Status"));
        Assert.Null(typeof(DeploymentEdit).GetProperty("Status"));
    }

    [Fact]
    public void The_four_fields_a_correction_may_touch_are_the_only_four()
    {
        Assert.Equal(
            ["At", "Note", "Ref", "Ticket"],
            typeof(DeploymentEdit).GetProperties().Select(property => property.Name).Order());
    }

    [Fact]
    public void A_correction_is_what_the_deployment_reports()
    {
        var deployment = A();

        var changes = deployment.Apply(
            new DeploymentEdit { Ref = "sha256:abc", Ticket = "LOG-42", At = Now.AddDays(-1) },
            Actor,
            Now.AddHours(1));

        Assert.Equal(["ref", "ticket", "at"], changes.Select(change => change.Field));
        Assert.Equal(Now.AddDays(-1), deployment.At);
        Assert.Equal(Now.AddHours(1), deployment.UpdatedAt);

        Assert.Empty(deployment.Apply(new DeploymentEdit(), Actor, Now.AddHours(2)));
        Assert.Equal(Now.AddHours(1), deployment.UpdatedAt);
    }

    [Fact]
    public void A_note_records_that_it_changed_and_not_how()
    {
        var note = Assert.Single(A().Apply(new DeploymentEdit { Note = "Rolled back." }, Actor, Now));

        Assert.Equal("note", note.Field);
        Assert.Null(note.OldValue);
        Assert.Null(note.NewValue);
    }
}
