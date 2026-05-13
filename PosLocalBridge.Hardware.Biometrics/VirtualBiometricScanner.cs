using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Hardware.Biometrics.Configuration;

namespace PosLocalBridge.Hardware.Biometrics;

/// <summary>
/// Emulates a fingerprint scanner by firing <see cref="OnFingerprintScanned"/>
/// on a fixed interval with a dummy Base64 template. Used for end-to-end testing
/// of the access flow without physical hardware.
/// </summary>
public sealed class VirtualBiometricScanner : IBiometricScanner
{
    private static readonly string DummyTemplate =
        Convert.ToBase64String(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE });

    private readonly ILogger<VirtualBiometricScanner> _logger;
    private readonly BiometricsConfig _config;

    private CancellationTokenSource? _internalCts;
    private Task? _loopTask;
    private PeriodicTimer? _timer;
    private bool _initialized;

    public event Func<string, Task>? OnFingerprintScanned;

    public VirtualBiometricScanner(
        IOptions<BiometricsConfig> options,
        ILogger<VirtualBiometricScanner> logger)
    {
        _logger = logger;
        _config = options.Value;
    }

    public Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;

        var seconds = Math.Max(1, _config.ScanIntervalSeconds);
        _logger.LogInformation(
            "VirtualBiometricScanner initialized. {IntervalSeconds}s {Mode}",
            seconds, _config.Mode);

        _internalCts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        _loopTask = Task.Run(() => LoopAsync(_internalCts.Token));

        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await FireAsync(DummyTemplate).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "VirtualBiometricScanner loop faulted. {ExceptionType}",
                ex.GetType().Name);
        }
    }

    private async Task FireAsync(string template)
    {
        var handler = OnFingerprintScanned;
        if (handler is null) return;

        try
        {
            await handler.Invoke(template).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OnFingerprintScanned handler threw. {ExceptionType}",
                ex.GetType().Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _internalCts?.Cancel();

        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        _timer?.Dispose();
        _internalCts?.Dispose();
    }
}
