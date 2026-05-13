using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Transport.Configuration;

namespace PosLocalBridge.Host;

public sealed class BiometricSupervisor : IHostedService, IDisposable
{
    private readonly ICloudClient _cloudClient;
    private readonly IBiometricScanner _scanner;
    private readonly IOptions<BridgeConfig> _bridgeConfig;
    private readonly ILogger<BiometricSupervisor> _logger;
    private readonly CancellationTokenSource _cts;

    private Func<string, Task>? _handler;

    public BiometricSupervisor(
        ICloudClient cloudClient,
        IBiometricScanner scanner,
        IOptions<BridgeConfig> bridgeConfig,
        IHostApplicationLifetime lifetime,
        ILogger<BiometricSupervisor> logger)
    {
        _cloudClient = cloudClient;
        _scanner = scanner;
        _bridgeConfig = bridgeConfig;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_handler is not null) return;

        _logger.LogInformation(
            "BiometricSupervisor starting. Subscribing to scanner events. {CloudMethod}",
            CloudMethods.ProcessFingerprint);

        _handler = async (template) =>
        {
            try
            {
                await _cloudClient.SendAsync(
                    CloudMethods.ProcessFingerprint,
                    new FingerprintPayload(template, _bridgeConfig.Value.BranchId),
                    _cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to send fingerprint to cloud. {ExceptionType}",
                    ex.GetType().Name);
            }
        };

        _scanner.OnFingerprintScanned += _handler;

        try
        {
            await _scanner.InitializeAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Biometric scanner failed to initialize. Scanner remains inactive. {ExceptionType}",
                ex.GetType().Name);
            // No re-throw — other devices keep working (CLAUDE.md regla #1).
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BiometricSupervisor stopping.");

        if (_handler is not null)
        {
            _scanner.OnFingerprintScanned -= _handler;
            _handler = null;
        }

        _cts.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_handler is not null)
        {
            _scanner.OnFingerprintScanned -= _handler;
            _handler = null;
        }
        _cts.Dispose();
    }
}
