using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;

namespace PosLocalBridge.Host;

public sealed class SerialInputSupervisor : IHostedService, IDisposable
{
    private readonly ICloudClient _cloudClient;
    private readonly ISerialInputScanner _scanner;
    private readonly ILogger<SerialInputSupervisor> _logger;
    private readonly CancellationTokenSource _cts;

    private Func<string, string, Task>? _handler;

    public SerialInputSupervisor(
        ICloudClient cloudClient,
        ISerialInputScanner scanner,
        IHostApplicationLifetime lifetime,
        ILogger<SerialInputSupervisor> logger)
    {
        _cloudClient = cloudClient;
        _scanner = scanner;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null) return;

        _logger.LogInformation(
            "SerialInputSupervisor starting. Subscribing to scanner events. {CloudMethod}",
            CloudMethods.ProcessSerialInput);

        _handler = async (deviceId, data) =>
        {
            try
            {
                await _cloudClient.SendAsync(
                    CloudMethods.ProcessSerialInput,
                    new SerialInputPayload(deviceId, data),
                    _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send serial input to cloud. {DeviceId} {ExceptionType}",
                    deviceId, ex.GetType().Name);
            }
        };

        _scanner.OnDataReceived += _handler;

        try
        {
            await _scanner.StartListeningAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Serial scanner failed to start. Scanner remains inactive. {ExceptionType}",
                ex.GetType().Name);
            // No re-throw — other devices keep working (CLAUDE.md regla #1).
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SerialInputSupervisor stopping.");

        if (_handler is not null)
        {
            _scanner.OnDataReceived -= _handler;
            _handler = null;
        }

        _cts.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_handler is not null)
        {
            _scanner.OnDataReceived -= _handler;
            _handler = null;
        }
        _cts.Dispose();
    }
}
