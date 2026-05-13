using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosLocalBridge.Contracts.Security;
using PosLocalBridge.Security.Configuration;
using PosLocalBridge.Transport.Configuration;

namespace PosLocalBridge.Security;

public sealed class HttpPairingService : IPairingService
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly BridgeConfig _bridgeConfig;
    private readonly SecurityConfig _securityConfig;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<HttpPairingService> _logger;

    public HttpPairingService(
        HttpClient httpClient,
        IOptions<BridgeConfig> bridgeOptions,
        IOptions<SecurityConfig> securityOptions,
        ITokenStore tokenStore,
        ILogger<HttpPairingService> logger)
    {
        _httpClient = httpClient;
        _bridgeConfig = bridgeOptions.Value;
        _securityConfig = securityOptions.Value;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task<bool> TryPairDeviceAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_securityConfig.PairingPin))
        {
            _logger.LogWarning("Pairing PIN is empty — cannot pair device.");
            return false;
        }

        var url = $"{_bridgeConfig.ApiBaseUrl.TrimEnd('/')}/api/devices/pair";

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                url,
                new { pin = _securityConfig.PairingPin },
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<PairingResponse>(ResponseOptions, ct)
                .ConfigureAwait(false);

            if (payload is null || string.IsNullOrEmpty(payload.Token))
            {
                _logger.LogWarning("Pairing response did not include a token.");
                return false;
            }

            await _tokenStore.SaveTokenAsync(payload.Token).ConfigureAwait(false);
            _logger.LogInformation("Device paired and token persisted.");
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Pairing HTTP error. {ExceptionType} {StatusCode}",
                ex.GetType().Name, ex.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Pairing failed. {ExceptionType}",
                ex.GetType().Name);
            return false;
        }
    }

    private sealed record PairingResponse(string Token);
}
