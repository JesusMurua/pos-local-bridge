using System.Net.Sockets;

namespace PosLocalBridge.Hardware.Printers.Connections;

/// <summary>
/// Lazy-connecting TCP printer connection. Opens a fresh <see cref="TcpClient"/> on
/// every <see cref="WriteAsync"/> call, bounded by a 5-second linked token covering
/// both connect and write. There is no persistent connection state, so DisposeAsync
/// is a no-op (required by <see cref="IAsyncDisposable"/>).
/// </summary>
internal sealed class TcpPrinterConnection : IPrinterConnection
{
    private const int OperationTimeoutMs = 5000;

    private readonly string _address;
    private readonly int _port;

    public TcpPrinterConnection(string address, int port)
    {
        _address = address;
        _port = port;
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        opCts.CancelAfter(TimeSpan.FromMilliseconds(OperationTimeoutMs));

        await client.ConnectAsync(_address, _port, opCts.Token).ConfigureAwait(false);

        var stream = client.GetStream();
        stream.WriteTimeout = OperationTimeoutMs;
        await stream.WriteAsync(data, opCts.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
