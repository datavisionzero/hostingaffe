using Microsoft.EntityFrameworkCore;
using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence;

/// <inheritdoc cref="IDeviceLogins"/>
public sealed class DeviceLogins(HostingaffeDbContext context) : IDeviceLogins
{
    public async Task AddAsync(DeviceLogin login, CancellationToken cancellationToken)
    {
        context.DeviceLogins.Add(login);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<DeviceLogin?> FindByUserCodeAsync(string userCode, CancellationToken cancellationToken) =>
        context.DeviceLogins
            .Where(x => x.UserCode == userCode)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<ApprovedDeviceLogin?> FindByCodeHashAsync(
        byte[] deviceCodeHash, CancellationToken cancellationToken)
    {
        var row = await (
            from login in context.DeviceLogins
            where login.DeviceCodeHash == deviceCodeHash
            join user in context.Users on login.ApprovedByUserId equals user.Id into approved
            from user in approved.DefaultIfEmpty()
            select new { login, user }).SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new ApprovedDeviceLogin(row.login, row.user);
    }

    public Task RecordAsync(DeviceLogin login, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    /// <remarks>
    /// One transaction, and the claim is a guarded update rather than a save:
    /// two polls arriving at once would both have read an approved login, and
    /// what decides between them has to be the row. The one that updates
    /// nothing is told the token has already been collected, which is what a
    /// device code working once means.
    /// </remarks>
    public async Task RedeemAsync(DeviceLogin login, Token token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The claim first, and it sets only the moment: the token does not
        // exist yet, and `issued_token_id` is a foreign key. What the row is
        // pointed at follows with the insert below, in the same transaction.
        var claimed = await context.DeviceLogins
            .Where(x => x.Id == login.Id && x.RedeemedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RedeemedAt, login.RedeemedAt), cancellationToken);

        if (claimed == 0)
        {
            throw new Refusal(
                RefusalCode.DeviceExpired,
                "This login's token has already been collected. A device code works once.");
        }

        context.Tokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
