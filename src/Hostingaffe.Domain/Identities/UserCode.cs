namespace Hostingaffe.Domain.Identities;

/// <summary>
/// The short code <c>ha login</c> prints and a person types into a browser on
/// whatever machine has one (VISION 6.1). Eight characters, read as
/// <c>XXXX-XXXX</c>.
/// </summary>
/// <remarks>
/// <para>
/// The alphabet is the twenty consonants of RFC 8628, and both halves of that
/// earn their place. No vowel means a code can never come out as a word, which
/// is what every short random code a person reads aloud eventually does. No
/// digit means the confusions that make people retype — <c>0</c> and <c>O</c>,
/// <c>1</c> and <c>I</c>, <c>5</c> and <c>S</c>, <c>2</c> and <c>Z</c> — cannot
/// arise at all, because only one half of each pair is in the alphabet.
/// </para>
/// <para>
/// Eight characters is 20^8, about 2.6 × 10^10, and a login lives ten minutes.
/// That is not a token's kind of unguessable and does not have to be: what it
/// guards is a request that still needs a signed-in person to approve it. The
/// credential is <see cref="DeviceCode"/>, and it is 256 bits.
/// </para>
/// </remarks>
public static class UserCode
{
    /// <summary>Consonants only: no word comes out of it, and no digit is in it.</summary>
    public const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ";

    /// <summary>How many characters a code has, stored without its separator.</summary>
    public const int Length = 8;

    /// <summary>Where the dash goes when a person has to read it.</summary>
    public const int GroupLength = 4;

    /// <summary>A new code. The only place one is made.</summary>
    public static string Issue() =>
        string.Create(Length, 0, (code, _) =>
        {
            for (var character = 0; character < Length; character++)
            {
                code[character] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        });

    /// <summary>
    /// The stored spelling of what a person typed. Case, spaces and dashes are
    /// forgiven — they are how the code was shown, not what it is. Empty when
    /// what arrived is not a code at all.
    /// </summary>
    public static string Normalize(string? typed)
    {
        if (typed is null)
        {
            return string.Empty;
        }

        var code = new string([.. typed
            .Where(character => !char.IsWhiteSpace(character) && character is not '-')
            .Select(char.ToUpperInvariant)]);

        return code.Length == Length && code.All(Alphabet.Contains) ? code : string.Empty;
    }

    /// <summary>The code as a person is shown it: <c>XXXX-XXXX</c>.</summary>
    public static string ForReading(string code) =>
        code.Length == Length ? $"{code[..GroupLength]}-{code[GroupLength..]}" : code;
}
