using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence;

public sealed class OneTimeSecrets(HostingaffeDbContext context) : IOneTimeSecrets
{
    public async Task AddReplacingLiveAsync(OneTimeSecret secret, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await context.OneTimeSecrets.Where(x => x.UserId == secret.UserId && x.Purpose == secret.Purpose && x.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.UsedAt, now), ct);
        context.OneTimeSecrets.Add(secret); await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<OneTimeSecret?> ConsumeAsync(byte[] secretHash, OneTimeSecretPurpose purpose, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var purposeName = purpose switch
        {
            OneTimeSecretPurpose.Invitation => "invitation",
            OneTimeSecretPurpose.PasswordRecovery => "password_recovery",
            OneTimeSecretPurpose.EmailChange => "email_change",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
        };
        var secret = await context.OneTimeSecrets.FromSqlInterpolated($"select * from one_time_secret where secret_hash = {secretHash} and purpose = {purposeName} for update").SingleOrDefaultAsync(ct);
        if (secret is null || !secret.IsLive(now)) return null;
        secret.Consume(now); await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return secret;
    }
}

public sealed class BrowserSessions(HostingaffeDbContext context) : IBrowserSessions
{
    public async Task AddAsync(BrowserSession session, CancellationToken ct) { context.BrowserSessions.Add(session); await context.SaveChangesAsync(ct); }
    public async Task<SessionUser?> AuthenticateAsync(byte[] hash, DateTimeOffset now, CancellationToken ct)
    {
        var row = await (from session in context.BrowserSessions join user in context.Users on session.UserId equals user.Id where session.SecretHash == hash select new { session, user }).SingleOrDefaultAsync(ct);
        if (row is null || row.user.State != UserState.Active || !row.session.IsValid(now)) return null;
        if (row.session.Touch(now)) await context.SaveChangesAsync(ct);
        return new(row.session, row.user);
    }
    public Task<IReadOnlyList<BrowserSession>> ListAsync(Guid userId, CancellationToken ct) => ListCore(userId, ct);
    private async Task<IReadOnlyList<BrowserSession>> ListCore(Guid userId, CancellationToken ct) => await context.BrowserSessions.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task RevokeAsync(Guid id, Guid userId, DateTimeOffset now, CancellationToken ct) => context.BrowserSessions.Where(x => x.Id == id && x.UserId == userId && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    public Task RevokeAllAsync(Guid userId, Guid? except, DateTimeOffset now, CancellationToken ct) => context.BrowserSessions.Where(x => x.UserId == userId && x.Id != except && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
}
