using System.Security.Cryptography;
using System.Text;

namespace Hostingaffe.Domain.Identities;

/// <summary>
/// The credential half of a device login: the long secret <c>ha</c> keeps to
/// itself and polls with, while the person reads out the short
/// <see cref="UserCode"/>.
/// </summary>
/// <remarks>
/// It is not a <see cref="TokenSecret"/> and deliberately carries no
/// <c>ha_</c> prefix: nothing should teach a secret scanner to look for a
/// string that is worthless ten minutes after it was made, and nothing should
/// be able to mistake one for a token. Like a token it is kept as its SHA-256 —
/// the instance holds no credential it could read back.
/// </remarks>
public static class DeviceCode
{
    /// <summary>256 bits, for the reason a token carries 256.</summary>
    public const int RandomBytes = 32;

    /// <summary>A new device code. The only place one is made.</summary>
    public static string Issue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(RandomBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the row keeps, and what a poll is looked up by.</summary>
    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
