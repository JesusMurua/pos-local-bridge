using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PosLocalBridge.Contracts.Security;
using PosLocalBridge.Security;
using PosLocalBridge.Security.Configuration;
using PosLocalBridge.Transport.Configuration;

namespace PosLocalBridge.Tests.Security;

public class HttpPairingServiceTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OnSendAsync { get; set; }
            = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            CallCount++;
            return OnSendAsync(request, ct);
        }
    }

    private static (HttpPairingService service, StubHttpMessageHandler handler, ITokenStore tokenStore, HttpClient httpClient) CreateService(
        string pin = "123456",
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? onSendAsync = null)
    {
        var handler = new StubHttpMessageHandler();
        if (onSendAsync is not null) handler.OnSendAsync = onSendAsync;

        var httpClient = new HttpClient(handler);
        var bridgeOptions = Options.Create(new BridgeConfig { ApiBaseUrl = "https://api.fino.com" });
        var securityOptions = Options.Create(new SecurityConfig { PairingPin = pin });
        var tokenStore = Substitute.For<ITokenStore>();
        var logger = Substitute.For<ILogger<HttpPairingService>>();

        var service = new HttpPairingService(
            httpClient,
            bridgeOptions,
            securityOptions,
            tokenStore,
            logger);

        return (service, handler, tokenStore, httpClient);
    }

    [Fact]
    public async Task TryPairDeviceAsync_Returns200WithToken_SavesTokenAndReturnsTrue()
    {
        var (service, _, tokenStore, httpClient) = CreateService(
            onSendAsync: (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"token":"new-jwt"}""",
                    Encoding.UTF8,
                    "application/json"),
            }));
        using var _ = httpClient;

        var result = await service.TryPairDeviceAsync(CancellationToken.None);

        result.Should().BeTrue();
        await tokenStore.Received(1).SaveTokenAsync("new-jwt");
    }

    [Fact]
    public async Task TryPairDeviceAsync_Returns401_DoesNotSaveTokenAndReturnsFalse()
    {
        var (service, _, tokenStore, httpClient) = CreateService(
            onSendAsync: (req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var _ = httpClient;

        var result = await service.TryPairDeviceAsync(CancellationToken.None);

        result.Should().BeFalse();
        await tokenStore.DidNotReceive().SaveTokenAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task TryPairDeviceAsync_WithEmptyPin_ReturnsFalseWithoutHttpCall()
    {
        var (service, handler, tokenStore, httpClient) = CreateService(pin: "");
        using var _ = httpClient;

        var result = await service.TryPairDeviceAsync(CancellationToken.None);

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
        await tokenStore.DidNotReceive().SaveTokenAsync(Arg.Any<string>());
    }
}
