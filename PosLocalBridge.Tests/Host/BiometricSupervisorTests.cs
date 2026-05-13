using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Host;
using PosLocalBridge.Transport.Configuration;

namespace PosLocalBridge.Tests.Host;

public class BiometricSupervisorTests
{
    private const int HandlerSettleMs = 200;

    private static BiometricSupervisor CreateSupervisor(
        out ICloudClient cloudClient,
        out IBiometricScanner scanner,
        out ILogger<BiometricSupervisor> logger,
        int branchId = 42)
    {
        cloudClient = Substitute.For<ICloudClient>();
        scanner = Substitute.For<IBiometricScanner>();
        logger = Substitute.For<ILogger<BiometricSupervisor>>();

        var bridgeOptions = Options.Create(new BridgeConfig { BranchId = branchId });

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);

        return new BiometricSupervisor(cloudClient, scanner, bridgeOptions, lifetime, logger);
    }

    [Fact]
    public async Task StartAsync_OnFingerprintScanned_SendsFingerprintToCloud()
    {
        var supervisor = CreateSupervisor(out var cloudClient, out var scanner, out _);

        cloudClient.SendAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await supervisor.StartAsync(CancellationToken.None);

        scanner.OnFingerprintScanned += Raise.Event<Func<string, Task>>("dummy_template");
        await Task.Delay(HandlerSettleMs);

        await scanner.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        await cloudClient.Received(1).SendAsync(
            CloudMethods.ProcessFingerprint,
            Arg.Is<FingerprintPayload>(p => p.Template == "dummy_template" && p.BranchId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_CancelsInternalToken_CapturedTokenBecomesCancelled()
    {
        var supervisor = CreateSupervisor(out var cloudClient, out var scanner, out _);

        CancellationToken capturedToken = default;
        cloudClient
            .SendAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Do<CancellationToken>(ct => capturedToken = ct))
            .Returns(Task.CompletedTask);

        await supervisor.StartAsync(CancellationToken.None);

        scanner.OnFingerprintScanned += Raise.Event<Func<string, Task>>("dummy_template");
        await Task.Delay(HandlerSettleMs);

        capturedToken.IsCancellationRequested.Should().BeFalse(
            "the supervisor should pass an active token before StopAsync");

        await supervisor.StopAsync(CancellationToken.None);

        capturedToken.IsCancellationRequested.Should().BeTrue(
            "StopAsync must cancel the supervisor's internal CTS, which the captured token references");
    }
}
