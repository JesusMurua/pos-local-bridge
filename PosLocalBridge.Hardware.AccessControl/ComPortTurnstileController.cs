using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Hardware.AccessControl.Configuration;

namespace PosLocalBridge.Hardware.AccessControl;

/// <summary>
/// COM-port turnstile driver. Opens a stateless SerialPort per trigger,
/// writes the configured payload, and closes (via using).
///
/// Known limitation: <see cref="SerialPort.Dispose"/> on Windows can hang if
/// there are pending I/O operations on the port. For this write-only short-payload
/// usage, the practical risk is minimal.
/// </summary>
public sealed class ComPortTurnstileController : ITurnstileController, IDisposable
{
    private readonly ILogger<ComPortTurnstileController> _logger;
    private readonly TurnstileConfig _config;
    private readonly SemaphoreSlim _portLock = new(1, 1);
    private readonly byte[] _payload;

    public ComPortTurnstileController(
        IOptions<TurnstileConfig> options,
        ILogger<ComPortTurnstileController> logger)
    {
        _logger = logger;
        _config = options.Value;

        byte[] parsed;
        try
        {
            parsed = Convert.FromHexString(_config.HexPayload ?? "");
        }
        catch (FormatException ex)
        {
            _logger.LogError(
                ex,
                "Invalid HexPayload format; turnstile triggers will be ignored. {HexPayload}",
                _config.HexPayload);
            parsed = Array.Empty<byte>();
        }

        _payload = parsed;
    }

    /// <summary>
    /// Triggers the turnstile by opening the COM port and writing the configured payload.
    ///
    /// Note: <see cref="SerialPort.Open"/> is synchronous; we offload it via
    /// <see cref="Task.Run(Action, CancellationToken)"/>. The cancellation token cancels
    /// task scheduling but cannot interrupt a hung Open() call. <see cref="TurnstileConfig.WriteTimeoutMs"/>
    /// bounds the write only.
    /// </summary>
    public async Task TriggerAsync(CancellationToken ct)
    {
        if (_payload.Length == 0)
        {
            _logger.LogWarning(
                "Invalid config, ignoring trigger. {PortName}",
                _config.PortName);
            return;
        }

        var acquired = false;
        try
        {
            await _portLock.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            using var port = new SerialPort(_config.PortName, _config.BaudRate);
            port.WriteTimeout = _config.WriteTimeoutMs;

            await Task.Run(() => port.Open(), ct).ConfigureAwait(false);
            await port.BaseStream.WriteAsync(_payload, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Turnstile triggered. {PortName} {BaudRate} {PayloadBytes}",
                _config.PortName, _config.BaudRate, _payload.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown or supervisor cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Turnstile trigger failed. {PortName} {BaudRate} {ExceptionType}",
                _config.PortName, _config.BaudRate, ex.GetType().Name);
        }
        finally
        {
            if (acquired)
            {
                _portLock.Release();
            }
        }
    }

    public void Dispose()
    {
        _portLock.Dispose();
    }
}
