namespace Hostingaffe.Domain.Identities;

/// <summary>
/// Where one device login has got to. A poll receives exactly one of these, and
/// four of the five end the polling (<c>docs/api.md</c>, The device login).
/// </summary>
public enum DeviceLoginState
{
    /// <summary>Nobody has approved it yet. Keep polling.</summary>
    Pending,

    /// <summary>A user approved it. The next poll receives the token.</summary>
    Approved,

    /// <summary>A user refused it — they did not start this login.</summary>
    Denied,

    /// <summary>Nobody approved it in time.</summary>
    Expired,

    /// <summary>The token was handed over. A device code works once.</summary>
    Redeemed,
}
