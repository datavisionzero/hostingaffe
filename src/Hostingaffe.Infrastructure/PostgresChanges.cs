using Microsoft.Extensions.Logging;
using Npgsql;
using Hostingaffe.Application.Ports;

namespace Hostingaffe.Infrastructure;

/// <summary>
/// One dedicated PostgreSQL listener for the instance, fanned out to every
/// in-process waiter. NOTIFY is only an acceleration: cancellation remains the
/// waiter's deadline and callers always re-run their query after waking.
/// </summary>
public sealed class PostgresChanges(string connectionString, ILogger<PostgresChanges> logger)
    : IChanges, IAsyncDisposable
{
    private readonly string _listenerConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
    {
        // LISTEN requires session affinity. This connection is deliberately
        // outside Npgsql's pool and never participates in request transactions.
        Pooling = false,
        ApplicationName = "hostingaffe-change-listener",
    }.ConnectionString;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Registration> _projects = [];
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenSource _registrationsChanged = new();
    private Task? _listener;

    public Task EnsureListeningAsync(Guid projectId, CancellationToken cancellationToken)
    {
        Task listening;
        lock (_gate)
        {
            var registration = RegistrationFor(projectId);
            listening = registration.Listening.Task;
        }

        return listening.WaitAsync(cancellationToken);
    }

    public Task WaitAsync(Guid projectId, CancellationToken cancellationToken)
    {
        Task listening;
        Task change;
        lock (_gate)
        {
            var registration = RegistrationFor(projectId);
            listening = registration.Listening.Task;
            change = registration.Changed.Task;
        }

        return WaitWhenListeningAsync(listening, change, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        Task? listener;
        lock (_gate)
        {
            listener = _listener;
            _registrationsChanged.Cancel();
        }

        if (listener is not null)
        {
            await listener.ConfigureAwait(false);
        }

        _registrationsChanged.Dispose();
        _stopping.Dispose();
    }

    private async Task ListenAsync()
    {
        var reconnectDelay = TimeSpan.FromSeconds(1);

        while (!_stopping.IsCancellationRequested)
        {
            Guid[] projects;
            CancellationToken registrationsChanged;
            lock (_gate)
            {
                projects = [.. _projects.Keys];
                registrationsChanged = _registrationsChanged.Token;
            }

            try
            {
                await using var connection = new NpgsqlConnection(_listenerConnectionString);
                connection.Notification += (_, notification) => Notify(notification.Channel);
                await connection.OpenAsync(_stopping.Token).ConfigureAwait(false);

                foreach (var projectId in projects)
                {
                    await using var command = new NpgsqlCommand($"listen {Channel(projectId)}", connection);
                    await command.ExecuteNonQueryAsync(_stopping.Token).ConfigureAwait(false);
                }

                lock (_gate)
                {
                    foreach (var projectId in projects)
                    {
                        _projects[projectId].Listening.TrySetResult();
                    }
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(
                    _stopping.Token, registrationsChanged);
                while (!wait.IsCancellationRequested)
                {
                    await connection.WaitAsync(wait.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (registrationsChanged.IsCancellationRequested)
            {
                // A project was added. Reconnect and LISTEN to the complete set;
                // wake existing waiters so the reconnect gap cannot hide a change.
                NotifyAndReset(projects);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The PostgreSQL change listener disconnected; reconnecting.");
                NotifyAndReset();

                try
                {
                    await Task.Delay(reconnectDelay, _stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private void Notify(string channel)
    {
        Guid? project = null;
        lock (_gate)
        {
            foreach (var projectId in _projects.Keys)
            {
                if (channel == Channel(projectId))
                {
                    project = projectId;
                    break;
                }
            }

            if (project is { } id)
            {
                Pulse(id);
            }
        }
    }

    private void NotifyAndReset(IReadOnlyCollection<Guid>? only = null)
    {
        lock (_gate)
        {
            foreach (var projectId in only ?? _projects.Keys)
            {
                var current = _projects[projectId];
                _projects[projectId] = new Registration();
                current.Changed.TrySetResult();
            }
        }
    }

    private void Pulse(Guid projectId)
    {
        var registration = _projects[projectId];
        registration.Changed.TrySetResult();
        registration.Changed = NewSource();
    }

    private void SignalRegistrationChanged()
    {
        var previous = _registrationsChanged;
        _registrationsChanged = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    private Registration RegistrationFor(Guid projectId)
    {
        if (!_projects.TryGetValue(projectId, out var registration))
        {
            registration = new Registration();
            _projects.Add(projectId, registration);
            SignalRegistrationChanged();
        }

        _listener ??= Task.Run(ListenAsync);
        return registration;
    }

    private static TaskCompletionSource NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitWhenListeningAsync(
        Task listening, Task change, CancellationToken cancellationToken)
    {
        await listening.WaitAsync(cancellationToken).ConfigureAwait(false);
        await change.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class Registration
    {
        public TaskCompletionSource Listening { get; } = NewSource();

        public TaskCompletionSource Changed { get; set; } = NewSource();
    }

    internal static string Channel(Guid projectId) => $"hostingaffe_{projectId:n}";
}
