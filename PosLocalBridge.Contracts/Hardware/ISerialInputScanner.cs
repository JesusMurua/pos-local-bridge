namespace PosLocalBridge.Contracts.Hardware;

public interface ISerialInputScanner : IAsyncDisposable
{
    /// <remarks>
    /// Multicast async event: when multiple handlers subscribe, only the last
    /// handler's <see cref="Task"/> is observed by the invoker. Subscribers must
    /// be defensive — exceptions thrown from a handler are not automatically
    /// propagated to the invoker, and a faulty handler may starve later handlers
    /// in the multicast list.
    /// </remarks>
    event Func<string, string, Task>? OnDataReceived;

    /// <summary>
    /// Begins listening on every configured serial device. May be called once;
    /// subsequent calls are no-op.
    /// </summary>
    /// <remarks>
    /// No read timeout is set on the underlying ports; a device that is silent
    /// (connected but not sending data) cannot be distinguished from a healthy
    /// idle device and will only be released at process shutdown via the
    /// cancellation token.
    /// </remarks>
    Task StartListeningAsync(CancellationToken ct);
}
