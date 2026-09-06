using System.Security.Cryptography;
using System.Text;

namespace Hostingaffe.Domain.Identities;

/// <summary>
/// The secret half of a token: what is generated, what is shown once, and what
/// the row keeps of it (<c>docs/storage.md</c>, Identities and tokens).
/// </summary>
/// <remarks>
/// <para>
/// A generated secret is <c>ha_</c> followed by 43 characters from
/// <c>[A-Za-z0-9]</c> — 256 bits — made by the instance. The bootstrap token is
/// the one exception: the operator supplies it, in whatever shape, and it has
/// to be at least <see cref="MinimumLength"/> characters. Nothing else is asked
/// of a secret's shape, because the row is found by the hash and not by the
/// text (VISION 12).
/// </para>
/// <para>
/// What is stored is the SHA-256 and the first eight characters. A plain hash
/// rather than a slow one, deliberately: the secret has 256 bits of entropy and
/// is generated, not chosen, so there is nothing for a slow hash to protect
/// against — and authentication is on every request an agent makes.
/// </para>
/// </remarks>
public static class TokenSecret
{
    public const string Prefix = "ha_";

    public const int RandomLength = 43;

    /// <summary>What the operator's bootstrap secret has to be at least.</summary>
    public const int MinimumLength = 32;

    public const int MaximumLength = 200;

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    public static byte[] HashOf(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>
    /// The first eight characters, shown in lists so that tokens can be told
    /// apart without any of them being recoverable.
    /// </summary>
    public static string PrefixOf(string secret) => Checked(secret)[..Token.PrefixLength];

    public static bool IsAcceptable(string? secret) =>
        secret is not null && secret.Length >= MinimumLength && secret.Length <= MaximumLength;

    /// <exception cref="ArgumentException">
    /// <paramref name="secret"/> is shorter than <see cref="MinimumLength"/> or
    /// longer than <see cref="MaximumLength"/>.
    /// </exception>
    public static string Checked(string secret) =>
        IsAcceptable(secret)
            ? secret
            : throw new ArgumentException(
                $"A token secret is {MinimumLength} to {MaximumLength} characters.", nameof(secret));
}
