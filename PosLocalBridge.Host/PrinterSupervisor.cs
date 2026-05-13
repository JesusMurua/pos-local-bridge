using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;

namespace PosLocalBridge.Host;

public sealed class PrinterSupervisor : IHostedService, IDisposable
{
    private readonly ICloudClient _cloudClient;
    private readonly IPrinterRouter _router;
    private readonly ILogger<PrinterSupervisor> _logger;
    private readonly CancellationTokenSource _cts;
    private IDisposable? _subscription;

    public PrinterSupervisor(
        ICloudClient cloudClient,
        IPrinterRouter router,
        IHostApplicationLifetime lifetime,
        ILogger<PrinterSupervisor> logger)
    {
        _cloudClient = cloudClient;
        _router = router;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null) return Task.CompletedTask;

        _logger.LogInformation(
            "PrinterSupervisor starting. Subscribing to {CloudMethod}.",
            CloudMethods.SendEscPosCommand);

        _subscription = _cloudClient.On<EscPosPayload>(
            CloudMethods.SendEscPosCommand,
            async (payload, _) => await _router.RouteCommandAsync(payload, _cts.Token).ConfigureAwait(false));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels in-flight printer commands but does not wait for them to complete.
    /// Per-command latency is bounded by the connection's own timeouts
    /// (5s for TCP, <c>SerialPort.WriteTimeout</c> for COM).
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PrinterSupervisor stopping. Cancelling in-flight commands.");
        _cts.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _cts.Dispose();
    }
}
