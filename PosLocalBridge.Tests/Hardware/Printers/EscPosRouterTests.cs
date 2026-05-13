using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Hardware.Printers;
using PosLocalBridge.Hardware.Printers.Configuration;
using PosLocalBridge.Hardware.Printers.Connections;

namespace PosLocalBridge.Tests.Hardware.Printers;

public class EscPosRouterTests
{
    private static EscPosRouter CreateRouter(
        out IPrinterConnection connection,
        out IPrinterConnectionFactory factory,
        out ILogger<EscPosRouter> logger)
    {
        connection = Substitute.For<IPrinterConnection>();
        factory = Substitute.For<IPrinterConnectionFactory>();
        factory.Create(Arg.Any<PrinterConfig>()).Returns(connection);

        logger = Substitute.For<ILogger<EscPosRouter>>();

        var registry = new PrinterRegistryConfig
        {
            Printers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["KITCHEN_1"] = new PrinterConfig
                {
                    Type = "Tcp",
                    Address = "127.0.0.1",
                    PortOrBaud = 9100,
                },
            },
        };

        var monitor = Substitute.For<IOptionsMonitor<PrinterRegistryConfig>>();
        monitor.CurrentValue.Returns(registry);

        return new EscPosRouter(monitor, factory, logger);
    }

    private static int WarningLogCount<T>(ILogger<T> logger) =>
        logger.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                        && call.GetArguments().Length >= 1
                        && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);

    [Fact]
    public async Task RouteCommandAsync_WithInvalidBase64_LogsWarningAndReleasesLock()
    {
        var router = CreateRouter(out var connection, out _, out var logger);
        var payload = new EscPosPayload("KITCHEN_1", "INVALID!!!");

        await router.RouteCommandAsync(payload, CancellationToken.None);

        // Second call must not deadlock — proves the lock was released after the failure.
        await router.RouteCommandAsync(payload, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        await connection.DidNotReceive().WriteAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        WarningLogCount(logger).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RouteCommandAsync_WithEmptyPrinterId_LogsWarningAndReturns()
    {
        var router = CreateRouter(out var connection, out var factory, out var logger);
        var payload = new EscPosPayload("", "SGVsbG8=");

        await router.RouteCommandAsync(payload, CancellationToken.None);

        factory.DidNotReceive().Create(Arg.Any<PrinterConfig>());
        await connection.DidNotReceive().WriteAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>());
        WarningLogCount(logger).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RouteCommandAsync_WithValidPayload_DecodesAndWritesDecodedBytes()
    {
        var router = CreateRouter(out var connection, out var factory, out _);
        var payload = new EscPosPayload("KITCHEN_1", "SGVsbG8="); // base64 "Hello"
        var expected = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        await router.RouteCommandAsync(payload, CancellationToken.None);

        factory.Received(1).Create(Arg.Any<PrinterConfig>());
        await connection.Received(1).WriteAsync(
            Arg.Is<byte[]>(b => b.SequenceEqual(expected)),
            Arg.Any<CancellationToken>());
    }
}
