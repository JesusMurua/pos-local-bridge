using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Hardware.SerialInput.Configuration;

namespace PosLocalBridge.Hardware.SerialInput;

/// <summary>
/// Continuous COM-port listener. Launches one background task per configured device,
/// each running a re-opening loop: open port, read bytes, fire <see cref="OnDataReceived"/>,
/// retry on error after a 5s back-off.
/// </summary>
/// <remarks>
/// <see cref="SerialPort.Open"/> is synchronous internally; the cancellation token cancels
/// task scheduling but cannot interrupt a hung Open() call. Combined with the lack of
/// <c>ReadTimeout</c>, silent or hung devices are only detected at process shutdown.
/// Devices added to configuration after <see cref="StartListeningAsync"/> has been called
/// are not picked up automatically (snapshot at start, no hot-reload).
/// </remarks>
public sealed class ContinuousSerialScanner : ISerialInputScanner
{
    private readonly IOptionsMonitor<SerialInputRegistryConfig> _config;
    private readonly ILogger<ContinuousSerialScanner> _logger;
    private readonly Dictionary<string, Task> _listenerTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _internalCts = new();

    private bool _started;
    private CancellationTokenRegistration _externalRegistration;

    public event Func<string, string, Task>? OnDataReceived;

    public ContinuousSerialScanner(
        IOptionsMonitor<SerialInputRegistryConfig> config,
        ILogger<ContinuousSerialScanner> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task StartListeningAsync(CancellationToken ct)
    {
        if (_started) return Task.CompletedTask;
        _started = true;

        // Link external cancellation into internal CTS via a single registration.
        _externalRegistration = ct.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            _internalCts);

        var devices = _config.CurrentValue.Devices;
        _logger.LogInformation(
            "ContinuousSerialScanner starting. {DeviceCount} devices configured.",
            devices.Count);

        foreach (var (deviceId, deviceConfig) in devices)
        {
            if (string.IsNullOrEmpty(deviceConfig.PortName))
            {
                _logger.LogWarning(
                    "Skipping serial device with empty PortName. {DeviceId}",
                    deviceId);
                continue;
            }

            if (deviceConfig.BaudRate <= 0)
            {
                _logger.LogWarning(
                    "Skipping serial device with invalid BaudRate. {DeviceId} {BaudRate}",
                    deviceId, deviceConfig.BaudRate);
                continue;
            }

            var capturedId = deviceId;
            var capturedConfig = deviceConfig;
            _listenerTasks[deviceId] = Task.Run(
                () => ListenLoopAsync(capturedId, capturedConfig, _internalCts.Token));
        }

        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(
        string deviceId,
        SerialDeviceConfig deviceConfig,
        CancellationToken ct)
    {
        var buffer = new byte[1024];

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var port = new SerialPort(deviceConfig.PortName, deviceConfig.BaudRate);
                await Task.Run(() => port.Open(), ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Serial port opened. {DeviceId} {PortName} {BaudRate}",
                    deviceId, deviceConfig.PortName, deviceConfig.BaudRate);

                while (!ct.IsCancellationRequested)
                {
                    var read = await port.BaseStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0) break; // EOF — outer loop will reopen.

                    var data = Encoding.UTF8.GetString(buffer, 0, read);
                    await FireAsync(deviceId, data).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Serial listener error. {DeviceId} {PortName} {ExceptionType}",
                    deviceId, deviceConfig.PortName, ex.GetType().Name);

                try
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task FireAsync(string deviceId, string data)
    {
        var handler = OnDataReceived;
        if (handler is null) return;

        try
        {
            await handler.Invoke(deviceId, data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OnDataReceived handler threw. {DeviceId} {ExceptionType}",
                deviceId, ex.GetType().Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _externalRegistration.Dispose();
        _internalCts.Cancel();

        if (_listenerTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(_listenerTasks.Values)
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Serial listener shutdown timed out after 10s.");
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Serial listener task faulted on shutdown. {ExceptionType}",
                    ex.GetType().Name);
            }
        }

        _internalCts.Dispose();
    }
}
