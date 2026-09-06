using Hostingaffe.Infrastructure.Security;

namespace Hostingaffe.IntegrationTests;

public sealed class Argon2idPasswordHasherTests
{
    [Fact]
    public async Task Encoded_hash_names_argon2id_and_verifies_without_storing_the_password()
    {
        var hasher = new Argon2idPasswordHasher();
        var encoded = await hasher.HashAsync("a sufficiently long password", TestContext.Current.CancellationToken);
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=1$", encoded);
        Assert.True(await hasher.VerifyAsync(encoded, "a sufficiently long password", TestContext.Current.CancellationToken));
        Assert.False(await hasher.VerifyAsync(encoded, "a different long password", TestContext.Current.CancellationToken));
    }
}
