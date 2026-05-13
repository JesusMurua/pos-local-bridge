using System.Net;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Security;
using PosLocalBridge.Transport.Configuration;

namespace PosLocalBridge.Transport.Cloud;

/// <summary>
/// SignalR client for the Fino cloud bridge hub.
///
/// Lifecycle assumes sequential invocation by <c>IHostedService</c>
/// (Start → Stop, no concurrent calls). Concurrent Start/Stop is not supported
/// and may race; the host pipeline guarantees the sequential contract.
/// </summary>
public sealed class FinoCloudClient : ICloudClient
{
    private const int StateStopped = 0;
    private const int StateStarted = 1;
    private const double MaxBackoffSeconds = 30.0;

    private readonly ILogger<FinoCloudClient> _logger;
    private readonly ITokenStore _tokenStore;
    private readonly HubConnection _hub;
    private readonly Uri _hubUrl;

    private int _state = StateStopped;
    private Task? _backgroundTask;

    public event Func<Task>? OnAuthRejected;

    public FinoCloudClient(
        IOptions<BridgeConfig> options,
        ITokenStore tokenStore,
        ILogger<FinoCloudClient> logger)
    {
        _logger = logger;
        _tokenStore = tokenStore;
        var config = options.Value;
        var baseUrl = config.ApiBaseUrl.TrimEnd('/');
        _hubUrl = new Uri($"{baseUrl}/hubs/bridge", UriKind.RelativeOrAbsolute);

        _hub = new HubConnectionBuilder()
            .WithUrl(_hubUrl, opts =>
            {
                opts.AccessTokenProvider = () => _tokenStore.GetTokenAsync();
            })
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        _hub.Reconnecting += OnReconnecting;
        _hub.Reconnected += OnReconnected;
        _hub.Closed += OnClosed;
    }

    public Task StartBackgroundAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, StateStarted, StateStopped) != StateStopped)
        {
            _logger.LogDebug("StartBackgroundAsync no-op; client already started.");
            return Task.CompletedTask;
        }

        _logger.LogInformation("SignalR background connect loop starting. {HubUrl}", _hubUrl);
        _backgroundTask = Task.Run(() => RunConnectLoopAsync(ct), ct);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, StateStopped, StateStarted) != StateStarted)
        {
            return;
        }

        _logger.LogInformation("SignalR client stopping. {HubUrl}", _hubUrl);

        try
        {
            await _hub.StopAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping SignalR hub. {ExceptionType}", ex.GetType().Name);
        }

        if (_backgroundTask is { } task)
        {
            try
            {
                await task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown — background loop honors the stoppingToken.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background connect loop faulted on shutdown. {ExceptionType}", ex.GetType().Name);
            }
        }
    }

    public IDisposable On(string methodName, Func<Task> handler) =>
        _hub.On(methodName, handler);

    public IDisposable On<T>(string methodName, Func<T, CancellationToken, Task> handler) =>
        _hub.On<T>(methodName, (T payload) => handler(payload, CancellationToken.None));

    public Task SendAsync(string methodName, object payload, CancellationToken ct) =>
        _hub.SendAsync(methodName, payload, ct);

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        _hub.Reconnecting -= OnReconnecting;
        _hub.Reconnected -= OnReconnected;
        _hub.Closed -= OnClosed;

        await _hub.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunConnectLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await _hub.StartAsync(ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "SignalR connected. {ConnectionId} {HubUrl} {Attempt}",
                    _hub.ConnectionId, _hubUrl, attempt);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await HandleAuthError(ex).ConfigureAwait(false);

                var delay = ComputeBackoff(attempt);
                _logger.LogWarning(
                    ex,
                    "SignalR initial connect failed. {Attempt} {DelayMs} {ExceptionType} {HubUrl}",
                    attempt, (int)delay.TotalMilliseconds, ex.GetType().Name, _hubUrl);

                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var baseSeconds = Math.Min(Math.Pow(2, attempt), MaxBackoffSeconds);
        var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2;
        var seconds = Math.Max(0.5, baseSeconds * (1.0 + jitter));
        return TimeSpan.FromSeconds(seconds);
    }

    private Task OnReconnecting(Exception? error)
    {
        _logger.LogWarning(
            error,
            "SignalR reconnecting. {ConnectionId} {HubUrl}",
            _hub.ConnectionId, _hubUrl);
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        _logger.LogInformation(
            "SignalR reconnected. {ConnectionId} {HubUrl}",
            connectionId, _hubUrl);
        return Task.CompletedTask;
    }

    private async Task OnClosed(Exception? error)
    {
        _logger.LogWarning(
            error,
            "SignalR closed. {ConnectionId} {HubUrl}",
            _hub.ConnectionId, _hubUrl);
        await HandleAuthError(error).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects authentication failure (HTTP 401 during SignalR negotiate) and
    /// invokes <see cref="OnAuthRejected"/> so the host can clear the token and
    /// restart.
    /// </summary>
    /// <remarks>
    /// Only covers <see cref="HttpRequestException"/> with status 401 (the common
    /// case during initial handshake). Auth failures during an established
    /// WebSocket session may surface as different exception types and are not
    /// currently detected — server-side revocation is best surfaced via a forced
    /// Closed event from the server.
    /// </remarks>
    private async Task HandleAuthError(Exception? ex)
    {
        if (ex is not HttpRequestException httpEx || httpEx.StatusCode != HttpStatusCode.Unauthorized)
        {
            return;
        }

        _logger.LogCritical("Token rejected (401). Notifying OnAuthRejected subscribers.");

        var handler = OnAuthRejected;
        if (handler is null) return;

        try
        {
            await handler.Invoke().ConfigureAwait(false);
        }
        catch (Exception handlerEx)
        {
            _logger.LogWarning(
                handlerEx,
                "OnAuthRejected handler threw. {ExceptionType}",
                handlerEx.GetType().Name);
        }
    }
}
