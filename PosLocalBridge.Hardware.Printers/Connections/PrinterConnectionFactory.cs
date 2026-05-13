using PosLocalBridge.Hardware.Printers.Configuration;

namespace PosLocalBridge.Hardware.Printers.Connections;

public sealed class PrinterConnectionFactory : IPrinterConnectionFactory
{
    public IPrinterConnection Create(PrinterConfig config)
    {
        if (string.Equals(config.Type, "Tcp", StringComparison.OrdinalIgnoreCase))
        {
            return new TcpPrinterConnection(config.Address, config.PortOrBaud);
        }

        if (string.Equals(config.Type, "Com", StringComparison.OrdinalIgnoreCase))
        {
            return new ComPrinterConnection(config.Address, config.PortOrBaud);
        }

        throw new NotSupportedException($"Unsupported printer type: '{config.Type}'.");
    }
}
