using Hostingaffe.Application.Ports;
using Hostingaffe.Domain;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Acts;

/// <summary>What <c>ha login</c> is told when a login begins.</summary>
/// <param name="DeviceCode">The long code <c>ha</c> keeps and polls with. Never shown to a person.</param>
/// <param name="UserCode">The short code <c>ha</c> prints, as a person reads it.</param>
/// <param name="VerificationUri">Where a person goes to approve, relative to the instance.</param>
/// <param name="VerificationUriComplete">The same, with the code already in it.</param>
/// <param name="ExpiresInSeconds">How long the person has.</param>
/// <param name="IntervalSeconds">How often <c>ha</c> should poll.</param>
public sealed record BegunDeviceLogin(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string VerificationUriComplete,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>What the browser is shown before somebody approves.</summary>
public sealed record WaitingDeviceLogin(
    string UserCode, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, DeviceLoginState State);

/// <summary>What a poll receives once a user has approved: the token, once.</summary>
public sealed record RedeemedDeviceLogin(IdentityRef User, IssuedToken Token);

/// <summary>
/// Begin a device login (ADR 0005). Unauthenticated by definition: the whole
/// flow exists to turn no credential into one.
/// </summary>
public sealed class BeginDeviceLogin(IDeviceLogins logins, TimeProvider clock)
{
    /// <summary>Where a person is sent. Relative: <c>ha</c> has the host already.</summary>
    public const string VerificationPath = "/device";

    public async Task<BegunDeviceLogin> ExecuteAsync(CancellationToken cancellationToken)
    {
        var (login, deviceCode, userCode) = DeviceLogin.Begin(clock.GetUtcNow());

        await logins.AddAsync(login, cancellationToken);

        var readable = UserCode.ForReading(userCode);

        return new BegunDeviceLogin(
            deviceCode,
            readable,
            VerificationPath,
            $"{VerificationPath}?code={readable}",
            (int)DeviceLogin.Lifetime.TotalSeconds,
            (int)DeviceLogin.PollingInterval.TotalSeconds);
    }
}

/// <summary>
/// What is waiting behind a code, for the screen that is about to ask whether
/// to approve it. It says when the login was begun and when it expires, and
/// nothing about the machine that began it — the instance knows nothing about
/// that machine, and a sentence invented here would be one a person could
/// believe.
/// </summary>
public sealed class ReadDeviceLogin(ICallerIdentity callerIdentity, IDeviceLogins logins, TimeProvider clock)
{
    public async Task<WaitingDeviceLogin> ExecuteAsync(string? typedCode, CancellationToken cancellationToken)
    {
        callerIdentity.Caller.RequireUser("approve a device login");

        var login = await Found(typedCode, logins, cancellationToken);

        return new WaitingDeviceLogin(
            UserCode.ForReading(login.UserCode), login.CreatedAt, login.ExpiresAt, login.StateAt(clock.GetUtcNow()));
    }

    /// <exception cref="Refusal"><c>not-found</c> when no login is waiting for that code.</exception>
    internal static async Task<DeviceLogin> Found(
        string? typedCode, IDeviceLogins logins, CancellationToken cancellationToken)
    {
        var code = UserCode.Normalize(typedCode);

        var login = code.Length == 0 ? null : await logins.FindByUserCodeAsync(code, cancellationToken);

        return login ?? throw new Refusal(RefusalCode.NotFound, "No login is waiting for that code.");
    }
}

/// <summary>
/// A user approves a device login, or says they did not start it.
/// </summary>
/// <remarks>
/// The caller is a user, always: what an approval hands over is a user token,
/// and an agent that could mint one has escaped its own identity
/// (planaffe ADR 0015). The token it produces is the approving user's own, so
/// there is nothing here an administrator has to be.
/// </remarks>
public sealed class DecideDeviceLogin(ICallerIdentity callerIdentity, IDeviceLogins logins, TimeProvider clock)
{
    public Task<WaitingDeviceLogin> ApproveAsync(string? typedCode, CancellationToken cancellationToken) =>
        DecideAsync(typedCode, approve: true, cancellationToken);

    public Task<WaitingDeviceLogin> DenyAsync(string? typedCode, CancellationToken cancellationToken) =>
        DecideAsync(typedCode, approve: false, cancellationToken);

    private async Task<WaitingDeviceLogin> DecideAsync(
        string? typedCode, bool approve, CancellationToken cancellationToken)
    {
        var caller = callerIdentity.Caller.RequireUser("approve a device login");

        var login = await ReadDeviceLogin.Found(typedCode, logins, cancellationToken);
        var now = clock.GetUtcNow();

        var decided = approve ? login.ApproveBy(caller.Id, now) : login.Deny(now);

        if (!decided)
        {
            // Which of the four it is, said as the code a client switches on:
            // a login that expired while the screen stood open is not the same
            // answer as one somebody has already dealt with.
            throw new Refusal(
                login.StateAt(now) switch
                {
                    DeviceLoginState.Denied => RefusalCode.DeviceDenied,
                    _ => RefusalCode.DeviceExpired,
                },
                "That login is no longer waiting to be approved.");
        }

        await logins.RecordAsync(login, cancellationToken);

        return new WaitingDeviceLogin(
            UserCode.ForReading(login.UserCode), login.CreatedAt, login.ExpiresAt, login.StateAt(now));
    }
}

/// <summary>
/// The poll: the user token once somebody has approved, and a code that says
/// why not until then.
/// </summary>
/// <remarks>
/// The token is the ordinary user token of <c>POST /tokens</c> — the same row,
/// revocable in the same list — because a second kind of token would be a
/// second thing to revoke and a second thing to explain.
/// </remarks>
public sealed class RedeemDeviceLogin(IDeviceLogins logins, TimeProvider clock)
{
    public async Task<RedeemedDeviceLogin> ExecuteAsync(string? deviceCode, CancellationToken cancellationToken)
    {
        var found = string.IsNullOrWhiteSpace(deviceCode)
            ? null
            : await logins.FindByCodeHashAsync(DeviceCode.Hash(deviceCode), cancellationToken);

        if (found is null)
        {
            throw new Refusal(RefusalCode.NotFound, "No login is waiting for that device code.");
        }

        var (login, approvedBy) = found;
        var now = clock.GetUtcNow();

        // Every state but one ends the polling, and each is its own code so
        // that `ha` waits on exactly one of them (docs/api.md).
        switch (login.StateAt(now))
        {
            case DeviceLoginState.Pending:
                throw new Refusal(RefusalCode.DevicePending, "Nobody has approved this login yet.");
            case DeviceLoginState.Denied:
                throw new Refusal(RefusalCode.DeviceDenied, "A user refused this login.");
            case DeviceLoginState.Expired:
                throw new Refusal(RefusalCode.DeviceExpired, "This login expired before it was approved.");
            case DeviceLoginState.Redeemed:
                throw new Refusal(
                    RefusalCode.DeviceExpired,
                    "This login's token has already been collected. A device code works once.");
        }

        var user = approvedBy
            ?? throw new Refusal(RefusalCode.NotFound, "The user who approved this login is no longer here.");

        if (user.State is not UserState.Active)
        {
            throw new Refusal(RefusalCode.DeviceDenied, "The user who approved this login is no longer active.");
        }

        var secret = TokenSecret.Generate();
        var token = Token.Issue(user, secret, now);

        login.RedeemTo(token.Id, now);

        await logins.RedeemAsync(login, token, cancellationToken);

        return new RedeemedDeviceLogin(
            IdentityRef.Of(user), new IssuedToken(token.Id, token.Prefix, secret, token.CreatedAt));
    }
}
