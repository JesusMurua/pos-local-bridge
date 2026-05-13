namespace PosLocalBridge.Contracts.Cloud;

public interface ICloudClient : IAsyncDisposable
{
    /// <summary>
    /// Raised when the cloud rejects the device token (currently HTTP 401 during
    /// SignalR negotiate). Subscribers should clear local credentials and trigger
    /// host shutdown so the service restarts and re-pairs on next boot.
    /// </summary>
    event Func<Task>? OnAuthRejected;

    Task StartBackgroundAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    IDisposable On(string methodName, Func<Task> handler);

    /// <remarks>
    /// The <see cref="CancellationToken"/> passed to the handler is currently always
    /// <see cref="CancellationToken.None"/> — SignalR does not natively flow a
    /// per-invocation cancellation token. Callers must supply their own lifecycle
    /// token (e.g., a supervisor-owned CTS) to the inner handler body if cancellation
    /// is needed.
    /// </remarks>
    IDisposable On<T>(string methodName, Func<T, CancellationToken, Task> handler);

    /// <summary>
    /// Sends a one-way invocation to the cloud hub. The hub must be connected;
    /// callers must catch and log exceptions defensively (e.g., when the hub is
    /// <c>Disconnected</c> or <c>Reconnecting</c>).
    /// </summary>
    Task SendAsync(string methodName, object payload, CancellationToken ct);
}
