using PosLocalBridge.Hardware.Printers.Configuration;

namespace PosLocalBridge.Hardware.Printers.Connections;

public interface IPrinterConnectionFactory
{
    IPrinterConnection Create(PrinterConfig config);
}
