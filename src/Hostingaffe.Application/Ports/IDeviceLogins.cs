using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Ports;

/// <summary>
/// A device login and the user who approved it, as a poll reads the two: one
/// lookup, because the token about to be issued belongs to that user.
/// </summary>
public sealed record ApprovedDeviceLogin(DeviceLogin Login, User? ApprovedBy);

/// <summary>
/// The device logins of ADR 0005: begun by a machine with no credential, found
/// by the short code a person types and by the hash of the long one <c>ha</c>
/// polls with.
/// </summary>
public interface IDeviceLogins
{
    Task AddAsync(DeviceLogin login, CancellationToken cancellationToken);

    /// <summary>By the code a person typed, already normalized.</summary>
    Task<DeviceLogin?> FindByUserCodeAsync(string userCode, CancellationToken cancellationToken);

    /// <summary>By the hash of the device code, with whoever approved it.</summary>
    Task<ApprovedDeviceLogin?> FindByCodeHashAsync(byte[] deviceCodeHash, CancellationToken cancellationToken);

    /// <summary>Writes back the approval or the refusal just made on <paramref name="login"/>.</summary>
    Task RecordAsync(DeviceLogin login, CancellationToken cancellationToken);

    /// <summary>
    /// The token and the redemption in one transaction: a device code that
    /// stayed usable after handing a token over would be a second token for
    /// whoever still holds it.
    /// </summary>
    Task RedeemAsync(DeviceLogin login, Token token, CancellationToken cancellationToken);
}
