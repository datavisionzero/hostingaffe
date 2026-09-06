using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Application.Ports;

public interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken cancellationToken);
    Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken);
}

public interface IOneTimeSecrets
{
    Task AddReplacingLiveAsync(OneTimeSecret secret, DateTimeOffset now, CancellationToken cancellationToken);
    Task<OneTimeSecret?> ConsumeAsync(byte[] secretHash, OneTimeSecretPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record SessionUser(BrowserSession Session, User User);

public interface IBrowserSessions
{
    Task AddAsync(BrowserSession session, CancellationToken cancellationToken);
    Task<SessionUser?> AuthenticateAsync(byte[] secretHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeAsync(Guid sessionId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeAllAsync(Guid userId, Guid? exceptSessionId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserSession>> ListAsync(Guid userId, CancellationToken cancellationToken);
}
