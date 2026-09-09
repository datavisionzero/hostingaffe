namespace Hostingaffe.Domain.Identities;

/// <summary>
/// One <c>ha login</c> in progress (ADR 0005): <c>ha</c> prints a short code, a
/// user approves it in a browser on whatever machine has one, and <c>ha</c>
/// polls with a long one until something has happened.
/// </summary>
/// <remarks>
/// <para>
/// This is the only sign-in that works everywhere <c>ha</c> runs — an SSH
/// session on a machine the team rents, a CI job, a container, an agent's
/// sandbox — because it is the only one that needs no browser on the machine
/// doing the asking.
/// </para>
/// <para>
/// The row keeps the hash of the device code and never the code, the way a
/// token row does. What it can read back is the short user code, which is not a
/// credential: it names a waiting login to the person approving it, and
/// approving takes their browser session.
/// </para>
/// </remarks>
public sealed class DeviceLogin
{
    /// <summary>
    /// How long a person has. Long enough to walk to another machine, short
    /// enough that an abandoned code is not lying around for an afternoon.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How often <c>ha</c> should poll. Seconds, and the instance says so.</summary>
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private DeviceLogin()
    {
        // EF Core materializes through this; every other route goes through Begin.
    }

    private DeviceLogin(byte[] deviceCodeHash, string userCode, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        DeviceCodeHash = deviceCodeHash;
        UserCode = userCode;
        CreatedAt = now;
        ExpiresAt = now.Add(Lifetime);
    }

    public Guid Id { get; private init; }

    /// <summary>The hash of the long code <c>ha</c> polls with, and never the code.</summary>
    public byte[] DeviceCodeHash { get; private init; } = null!;

    /// <summary>The short code a person reads out of a terminal and types into a browser.</summary>
    public string UserCode { get; private init; } = null!;

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset ExpiresAt { get; private init; }

    /// <summary>When a user approved it, and which one.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    /// <summary>When a user said they did not start this login.</summary>
    public DateTimeOffset? DeniedAt { get; private set; }

    /// <summary>When the token was handed over. A device code works once.</summary>
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>Which user token this login produced, once it has.</summary>
    public Guid? IssuedTokenId { get; private set; }

    /// <summary>Begin one: the row, and the two codes, which exist exactly once.</summary>
    /// <remarks>
    /// The device code is returned rather than kept, for the reason a token
    /// secret is: what the instance stores of it is its hash.
    /// </remarks>
    public static (DeviceLogin Login, string DeviceCode, string UserCode) Begin(DateTimeOffset now)
    {
        var deviceCode = Identities.DeviceCode.Issue();
        var userCode = Identities.UserCode.Issue();

        return (new DeviceLogin(Identities.DeviceCode.Hash(deviceCode), userCode, now), deviceCode, userCode);
    }

    /// <summary>Where this login has got to at <paramref name="moment"/>.</summary>
    /// <remarks>
    /// The order is the order of what already happened: a redeemed login stays
    /// redeemed once it expires, and an approval nobody collected in time is
    /// expired rather than approved — otherwise a device code left behind in a
    /// CI log would still be worth something an hour later.
    /// </remarks>
    public DeviceLoginState StateAt(DateTimeOffset moment) =>
        RedeemedAt is not null ? DeviceLoginState.Redeemed
        : DeniedAt is not null ? DeviceLoginState.Denied
        : moment >= ExpiresAt ? DeviceLoginState.Expired
        : ApprovedAt is not null ? DeviceLoginState.Approved
        : DeviceLoginState.Pending;

    /// <summary>
    /// A user approves it. Only a pending login can be approved: approving one
    /// twice, or one somebody already refused, changes nothing and says so.
    /// </summary>
    public bool ApproveBy(Guid userId, DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceLoginState.Pending)
        {
            return false;
        }

        ApprovedAt = moment;
        ApprovedByUserId = userId;

        return true;
    }

    /// <summary>A user says they did not start this login.</summary>
    public bool Deny(DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceLoginState.Pending)
        {
            return false;
        }

        DeniedAt = moment;

        return true;
    }

    /// <summary>
    /// Hand the token over, once. Only an approved login can be redeemed, and
    /// only the first poll after the approval receives anything.
    /// </summary>
    public bool RedeemTo(Guid tokenId, DateTimeOffset moment)
    {
        if (StateAt(moment) is not DeviceLoginState.Approved)
        {
            return false;
        }

        RedeemedAt = moment;
        IssuedTokenId = tokenId;

        return true;
    }
}
