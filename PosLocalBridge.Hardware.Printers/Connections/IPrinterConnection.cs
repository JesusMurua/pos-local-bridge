namespace PosLocalBridge.Hardware.Printers.Connections;

public interface IPrinterConnection : IAsyncDisposable
{
    Task WriteAsync(byte[] data, CancellationToken ct);
}
