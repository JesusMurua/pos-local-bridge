using System.IO.Ports;

namespace PosLocalBridge.Hardware.Printers.Connections;

/// <summary>
/// COM-port printer connection. Opens a stateless <see cref="SerialPort"/> on every
/// <see cref="WriteAsync"/> call, writes the payload, and closes (via using). No
/// persistent state, so DisposeAsync is a no-op.
/// </summary>
internal sealed class ComPrinterConnection : IPrinterConnection
{
    private const int WriteTimeoutMs = 2000;

    private readonly string _portName;
    private readonly int _baudRate;

    public ComPrinterConnection(string portName, int baudRate)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        using var port = new SerialPort(_portName, _baudRate);
        port.WriteTimeout = WriteTimeoutMs;

        await Task.Run(() => port.Open(), ct).ConfigureAwait(false);
        await port.BaseStream.WriteAsync(data, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
