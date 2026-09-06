using Hostingaffe.Application.Acts;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.UnitTests;

/// <summary>
/// The act against a substituted token store: what reaches the store, what does
/// not, and what a found row becomes.
/// </summary>
public sealed class AuthenticateTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly User Maintainer = User.Create("maintainer", administrator: true, Now);

    private static readonly string Secret = TokenSecret.Generate();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic ha_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ")]
    [InlineData("ha_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ")]
    [InlineData("Bearer too-short")]
    public async Task What_is_not_a_bearer_token_never_reaches_the_store(string? authorization)
    {
        var store = new Store(null);

        Assert.Null(await new AuthenticateToken(store, new IdentityStore()).ExecuteAsync(authorization, CancellationToken.None));
        Assert.Equal(0, store.Lookups);
    }

    [Fact]
    public async Task A_found_token_is_its_identity()
    {
        var token = Token.Issue(Maintainer, Secret, Now);
        var store = new Store(new PresentedToken(token, Maintainer));

        var caller = await new AuthenticateToken(store, new IdentityStore()).ExecuteAsync($"Bearer {Secret}", CancellationToken.None);

        Assert.NotNull(caller);
        Assert.Equal(Maintainer.Id, caller.Id);
        Assert.Equal(IdentityKind.User, caller.Kind);
        Assert.True(caller.Administrator);
        Assert.Null(caller.OwnerId);
        Assert.Equal(token.Id, caller.TokenId);
        Assert.Equal(Secret[..8], caller.TokenPrefix);
        Assert.Equal(TokenSecret.HashOf(Secret), store.LastHash);
    }

    [Fact]
    public async Task The_scheme_is_read_regardless_of_case()
    {
        var store = new Store(new PresentedToken(Token.Issue(Maintainer, Secret, Now), Maintainer));

        Assert.NotNull(await new AuthenticateToken(store, new IdentityStore()).ExecuteAsync($"bearer {Secret}", CancellationToken.None));
    }

    [Fact]
    public async Task A_revoked_token_admits_nobody()
    {
        var token = Token.Issue(Maintainer, Secret, Now);
        token.Revoke(Now.AddMinutes(1));
        var store = new Store(new PresentedToken(token, Maintainer));

        Assert.Null(await new AuthenticateToken(store, new IdentityStore()).ExecuteAsync($"Bearer {Secret}", CancellationToken.None));
    }

    [Fact]
    public async Task An_agents_token_carries_its_owner()
    {
        var agent = Agent.Create("quiet-otter-42", Maintainer.Id, Now);
        var store = new Store(new PresentedToken(Token.Issue(agent, Secret, Now), agent));

        var caller = await new AuthenticateToken(store, new IdentityStore(Maintainer)).ExecuteAsync($"Bearer {Secret}", CancellationToken.None);

        Assert.NotNull(caller);
        Assert.Equal(IdentityKind.Agent, caller.Kind);
        Assert.Equal(Maintainer.Id, caller.OwnerId);
        Assert.False(caller.Administrator);
    }

    private sealed class Store(PresentedToken? answer) : ITokens
    {
        public int Lookups { get; private set; }

        public byte[]? LastHash { get; private set; }

        public Task<PresentedToken?> FindByHashAsync(byte[] secretHash, CancellationToken cancellationToken)
        {
            Lookups++;
            LastHash = secretHash;
            return Task.FromResult(answer);
        }

        // Not on the authentication path.
        public Task<Token?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Token?> FindAgentTokenAsync(Guid agentId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Token>> ListUserTokensAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddAsync(Token token, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RecordRevocationAsync(Token token, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class IdentityStore(User? owner = null) : IIdentities
    {
        public Task<User?> FindUserAsync(Guid id, CancellationToken ct) => Task.FromResult(owner?.Id == id ? owner : null);
        public Task<bool> AnyAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Identity?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Agent?> FindAgentAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Identity?> FindByNameAsync(string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, Identity>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NameTakenAsync(string name, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRow>> ListAgentsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(Identity identity, Token token, CancellationToken ct) => throw new NotSupportedException();
        public Task RecordRenameAsync(Agent agent, CancellationToken ct) => throw new NotSupportedException();
        public Task RecordMetadataAsync(Agent agent, AgentMetadataReport report, CancellationToken ct) => throw new NotSupportedException();
    }
}
