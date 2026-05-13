namespace PosLocalBridge.Contracts.Hardware;

public interface IBiometricScanner : IAsyncDisposable
{
    /// <remarks>
    /// Multicast async event: when multiple handlers subscribe, only the last
    /// handler's <see cref="Task"/> is observed by the invoker. Subscribers must
    /// be defensive — exceptions thrown from a handler are not automatically
    /// propagated to the invoker, and a faulty handler may starve later handlers
    /// in the multicast list.
    /// </remarks>
    event Func<string, Task>? OnFingerprintScanned;

    /// <summary>
    /// Begins polling/listening for fingerprint events. May be called once;
    /// subsequent calls are no-op.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);
}
