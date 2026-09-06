using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class AgentMetadataTests
{
    [Fact]
    public void A_report_changes_only_present_fields_and_null_clears_one()
    {
        var agent = Agent.Create("agent", Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        agent.ReportMetadata(true, "codex", true, "cli", true, "container", true, "1.0", DateTimeOffset.UnixEpoch);

        var snapshot = agent.ReportMetadata(false, null, true, null, false, null, true, "1.1", DateTimeOffset.UnixEpoch.AddMinutes(1));

        Assert.Equal(new AgentMetadata("codex", null, "container", "1.1"), snapshot);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), agent.MetadataReportedAt);
    }

    [Fact]
    public void A_metadata_value_is_at_most_one_hundred_characters()
    {
        var agent = Agent.Create("agent", Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var invalid = Assert.Throws<ArgumentException>(() =>
            agent.ReportMetadata(true, new string('x', 101), false, null, false, null, false, null, DateTimeOffset.UnixEpoch));
        Assert.Equal("kind", invalid.ParamName);
    }
}
