using Hostingaffe.Domain.Providers;

namespace Hostingaffe.Application.Ports;

/// <summary>Provider records, including deleted rows for restoration and key reservation.</summary>
public interface IProviders
{
    Task<Provider?> FindAnyAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<Provider>> ListAsync(CancellationToken cancellationToken);
    Task<bool> InUseAsync(string key, CancellationToken cancellationToken);
    void Add(Provider provider);
    Task SaveAsync(CancellationToken cancellationToken);
}
