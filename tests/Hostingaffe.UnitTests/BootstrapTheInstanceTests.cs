using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

public sealed class BootstrapTheInstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Secret = "a-bootstrap-secret-of-thirty-two-characters-or-more";

    [Fact]
    public async Task An_empty_instance_gets_its_administrator_and_their_token()
    {
        var store = new Store(any: false);

        var outcome = await Act(store).ExecuteAsync(new BootstrapSettings("maintainer", Secret, "maintainer@example.test"), CancellationToken.None);

        Assert.Equal(BootstrapOutcome.Bootstrapped, outcome);
        Assert.NotNull(store.Added);
        var user = Assert.IsType<User>(store.Added.Value.Identity);
        Assert.Equal("maintainer", user.Name);
        Assert.True(user.Administrator);
        Assert.Equal(Now, user.CreatedAt);

        var token = store.Added.Value.Token;
        Assert.Equal(user.Id, token.IdentityId);
        Assert.Equal(IdentityKind.User, token.Kind);
        Assert.Equal(Secret[..8], token.Prefix);
        Assert.Equal(TokenSecret.HashOf(Secret), token.SecretHash);
    }

    [Fact]
    public async Task A_bootstrapped_instance_ignores_the_environment_whatever_it_says()
    {
        var store = new Store(any: true);

        var outcome = await Act(store).ExecuteAsync(new BootstrapSettings("somebody", "short", "somebody@example.test"), CancellationToken.None);

        Assert.Equal(BootstrapOutcome.AlreadyBootstrapped, outcome);
        Assert.Null(store.Added);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("maintainer", null)]
    [InlineData(null, Secret)]
    [InlineData("", Secret)]
    [InlineData("maintainer", "  ")]
    public async Task Without_both_variables_there_is_nothing_to_bootstrap_from(string? name, string? secret)
    {
        var store = new Store(any: false);

        var outcome = await Act(store).ExecuteAsync(new BootstrapSettings(name, secret), CancellationToken.None);

        Assert.Equal(BootstrapOutcome.NothingToBootstrapFrom, outcome);
        Assert.Null(store.Added);
    }

    [Fact]
    public async Task A_short_secret_is_refused_and_nothing_is_written()
    {
        var store = new Store(any: false);

        var refusal = await Assert.ThrowsAsync<BootstrapRefusedException>(() =>
            Act(store).ExecuteAsync(new BootstrapSettings("maintainer", "only-nine", "maintainer@example.test"), CancellationToken.None));

        Assert.Contains("HOSTINGAFFE_BOOTSTRAP_TOKEN", refusal.Message, StringComparison.Ordinal);
        Assert.Null(store.Added);
    }

    private static BootstrapTheInstance Act(Store store) => new(store, new StoppedClock(Now));

    private sealed class Store(bool any) : IIdentities
    {
        public (Identity Identity, Token Token)? Added { get; private set; }

        public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(any);

        public Task<Identity?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Identity?>(null);

        public Task AddAsync(Identity identity, Token token, CancellationToken cancellationToken)
        {
            Added = (identity, token);
            return Task.CompletedTask;
        }

        // Not on the bootstrap path.
        public Task<Agent?> FindAgentAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> NameTakenAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Identity?> FindByNameAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, Identity>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentRow>> ListAgentsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordRenameAsync(Agent agent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordMetadataAsync(Agent agent, AgentMetadataReport report, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
