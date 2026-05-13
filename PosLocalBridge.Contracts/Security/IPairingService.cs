namespace PosLocalBridge.Contracts.Security;

public interface IPairingService
{
    Task<bool> TryPairDeviceAsync(CancellationToken ct);
}
