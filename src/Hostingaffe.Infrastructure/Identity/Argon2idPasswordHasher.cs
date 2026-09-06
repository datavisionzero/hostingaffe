using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Infrastructure.Security;

/// <summary>Argon2id with a PHC-style self-describing encoded value.</summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const int MemoryKiB = 65536, Iterations = 3, Parallelism = 1, HashBytes = 32, SaltBytes = 16;
    public Task<string> HashAsync(string password, CancellationToken cancellationToken)
    {
        Validate(password); var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return EncodeAsync(password, salt, MemoryKiB, Iterations, Parallelism, cancellationToken);
    }
    public async Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken)
    {
        Validate(password);
        try {
            var parts = encodedHash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19") return false;
            var parameters = parts[3].Split(',').Select(x => x.Split('=')).ToDictionary(x => x[0], x => int.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture));
            var salt = Convert.FromBase64String(parts[4]); var expected = Convert.FromBase64String(parts[5]);
            if (parameters["m"] is < 8192 or > 262144 || parameters["t"] is < 1 or > 10 || parameters["p"] is < 1 or > 16
                || salt.Length is < 16 or > 64 || expected.Length != HashBytes) return false;
            var actualEncoded = await EncodeAsync(password, salt, parameters["m"], parameters["t"], parameters["p"], cancellationToken);
            var actual = Convert.FromBase64String(actualEncoded.Split('$')[5]);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        } catch (FormatException) { return false; } catch (KeyNotFoundException) { return false; }
        catch (OverflowException) { return false; }
    }
    private static async Task<string> EncodeAsync(string password, byte[] salt, int memory, int iterations, int parallelism, CancellationToken ct)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = memory, Iterations = iterations, DegreeOfParallelism = parallelism };
        ct.ThrowIfCancellationRequested();
        var hash = await argon.GetBytesAsync(HashBytes);
        ct.ThrowIfCancellationRequested();
        return $"$argon2id$v=19$m={memory},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    private static void Validate(string password) { if (password is null || password.Length < 12) throw new ArgumentException("A password is at least 12 characters.", nameof(password)); }
}
