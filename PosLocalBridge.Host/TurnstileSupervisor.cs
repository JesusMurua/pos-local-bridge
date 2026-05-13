using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;

namespace PosLocalBridge.Host;

public sealed class TurnstileSupervisor : IHostedService, IDisposable
{
    private readonly ICloudClient _cloudClient;
    private readonly ITurnstileController _turnstileController;
    private readonly ILogger<TurnstileSupervisor> _logger;
    private readonly CancellationTokenSource _cts = new();
    private IDisposable? _subscription;

    public TurnstileSupervisor(
        ICloudClient cloudClient,
        ITurnstileController turnstileController,
        ILogger<TurnstileSupervisor> logger)
    {
        _cloudClient = cloudClient;
        _turnstileController = turnstileController;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "TurnstileSupervisor starting. Subscribing to {CloudMethod}.",
            CloudMethods.OpenTurnstile);

        _subscription = _cloudClient.On(
            CloudMethods.OpenTurnstile,
            () => _turnstileController.TriggerAsync(_cts.Token));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels in-flight triggers but does not wait for them to complete.
    /// Trigger latency is bounded by <c>TurnstileConfig.WriteTimeoutMs</c>.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TurnstileSupervisor stopping. Cancelling in-flight triggers.");
        _cts.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _cts.Dispose();
    }
}
