using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence;

/// <summary>
/// The token rows: found by the hash — one statement on <c>token_secret_hash</c>
/// joined to the identity row, per authentication — and by id or identity for
/// the acts that list and revoke them.
/// </summary>
public sealed class Tokens(HostingaffeDbContext context) : ITokens
{
    public Task<PresentedToken?> FindByHashAsync(byte[] secretHash, CancellationToken cancellationToken) =>
        context.Tokens
            .Where(t => t.SecretHash == secretHash)
            .Join(context.Identities, t => t.IdentityId, i => i.Id, (t, i) => new PresentedToken(t, i))
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Token?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tokens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Token?> FindAgentTokenAsync(Guid agentId, CancellationToken cancellationToken) =>
        context.Tokens.SingleOrDefaultAsync(
            t => t.IdentityId == agentId && t.Kind == IdentityKind.Agent, cancellationToken);

    public async Task<IReadOnlyList<Token>> ListUserTokensAsync(Guid userId, CancellationToken cancellationToken) =>
        await context.Tokens
            .Where(t => t.IdentityId == userId)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Token token, CancellationToken cancellationToken)
    {
        context.Tokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task RecordRevocationAsync(Token token, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
