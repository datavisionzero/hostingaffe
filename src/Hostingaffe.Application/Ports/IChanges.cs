namespace Hostingaffe.Application.Ports;

/// <summary>
/// The wake-up channel for a project's changing work. A notification carries
/// no state: after this returns, the caller asks its original question again.
/// </summary>
public interface IChanges
{
    /// <summary>Ensure the project channel is being listened to before a caller checks its state.</summary>
    Task EnsureListeningAsync(Guid projectId, CancellationToken cancellationToken);

    Task WaitAsync(Guid projectId, CancellationToken cancellationToken);
}
