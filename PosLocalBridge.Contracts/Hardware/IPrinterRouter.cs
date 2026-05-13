namespace PosLocalBridge.Contracts.Hardware;

public interface IPrinterRouter
{
    Task RouteCommandAsync(EscPosPayload payload, CancellationToken ct);
}
