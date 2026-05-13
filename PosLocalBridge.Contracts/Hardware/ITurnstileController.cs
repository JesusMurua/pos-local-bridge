namespace PosLocalBridge.Contracts.Hardware;

public interface ITurnstileController
{
    Task TriggerAsync(CancellationToken ct);
}
