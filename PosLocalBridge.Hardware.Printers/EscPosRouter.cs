using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Hardware.Printers.Configuration;
using PosLocalBridge.Hardware.Printers.Connections;

namespace PosLocalBridge.Hardware.Printers;

/// <summary>
/// Routes ESC/POS raw byte commands to the matching printer connection,
/// serializing concurrent calls on a per-printer <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class EscPosRouter : IPrinterRouter, IDisposable
{
    private readonly IOptionsMonitor<PrinterRegistryConfig> _config;
    private readonly IPrinterConnectionFactory _factory;
    private readonly ILogger<EscPosRouter> _logger;

    private readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim>> _locks
        = new(StringComparer.OrdinalIgnoreCase);

    public EscPosRouter(
        IOptionsMonitor<PrinterRegistryConfig> config,
        IPrinterConnectionFactory factory,
        ILogger<EscPosRouter> logger)
    {
        _config = config;
        _factory = factory;
        _logger = logger;
    }

    public async Task RouteCommandAsync(EscPosPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payload.PrinterId) || string.IsNullOrEmpty(payload.Base64Bytes))
        {
            _logger.LogWarning("Invalid ESC/POS payload (empty PrinterId or Base64Bytes).");
            return;
        }

        if (!_config.CurrentValue.Printers.TryGetValue(payload.PrinterId, out var config))
        {
            _logger.LogWarning("Printer not found in registry. {PrinterId}", payload.PrinterId);
            return;
        }

        var semaphore = _locks
            .GetOrAdd(payload.PrinterId, _ => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1)))
            .Value;

        var acquired = false;
        try
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            var bytes = Convert.FromBase64String(payload.Base64Bytes);

            await using var connection = _factory.Create(config);
            await connection.WriteAsync(bytes, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "ESC/POS command routed. {PrinterId} {Type} {Address} {Bytes}",
                payload.PrinterId, config.Type, config.Address, bytes.Length);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown or supervisor cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Printer route failed. {PrinterId} {ExceptionType}",
                payload.PrinterId, ex.GetType().Name);
        }
        finally
        {
            if (acquired)
            {
                semaphore.Release();
            }
        }
    }

    public void Dispose()
    {
        foreach (var lazy in _locks.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }
        _locks.Clear();
    }
}
