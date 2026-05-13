using Microsoft.Extensions.DependencyInjection;
using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Security;

namespace PosLocalBridge.Host;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ICloudClient _cloudClient;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITokenStore _tokenStore;

    public Worker(
        ILogger<Worker> logger,
        ICloudClient cloudClient,
        IHostApplicationLifetime lifetime,
        IServiceScopeFactory scopeFactory,
        ITokenStore tokenStore)
    {
        _logger = logger;
        _cloudClient = cloudClient;
        _lifetime = lifetime;
        _scopeFactory = scopeFactory;
        _tokenStore = tokenStore;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker starting.");

        // Wire auth-rejection recovery BEFORE any hub activity could fire it.
        _cloudClient.OnAuthRejected += HandleAuthRejected;

        // Pairing check FIRST — fail fast if no token and pairing fails.
        var token = await _tokenStore.GetTokenAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogInformation("No stored token — attempting device pairing.");

            // Scope keeps the Transient IPairingService (registered via AddHttpClient)
            // from becoming a captive dependency of the Singleton Worker.
            using var scope = _scopeFactory.CreateScope();
            var pairing = scope.ServiceProvider.GetRequiredService<IPairingService>();
            var paired = await pairing.TryPairDeviceAsync(cancellationToken).ConfigureAwait(false);
            if (!paired)
            {
                _logger.LogCritical("Device is not paired and pairing failed. Stopping.");
                _lifetime.StopApplication();
                return;
            }

            _logger.LogInformation("Device paired successfully.");
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _cloudClient.StartBackgroundAsync(_lifetime.ApplicationStopping).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker stopping.");
        _cloudClient.OnAuthRejected -= HandleAuthRejected;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _cloudClient.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleAuthRejected()
    {
        try
        {
            await _tokenStore.ClearTokenAsync().ConfigureAwait(false);
            _logger.LogCritical("Cleared token after 401. Restarting host.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear token after 401. Restarting anyway. {ExceptionType}",
                ex.GetType().Name);
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
