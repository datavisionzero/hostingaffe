using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class AgentNameTests
{
    [Fact]
    public void An_assigned_name_is_two_words_and_a_number_and_a_valid_identity_name()
    {
        var random = new Random(42);

        for (var i = 0; i < 200; i++)
        {
            var name = AgentName.Assign(random);

            Assert.Matches("^[a-z]+-[a-z]+-[1-9][0-9]?$", name);
            Assert.Equal(name, Identity.NormalizeName(name));
        }
    }

    [Fact]
    public void Names_vary()
    {
        var random = new Random(7);

        Assert.True(Enumerable.Range(0, 50).Select(_ => AgentName.Assign(random)).Distinct().Count() > 40);
    }
}
