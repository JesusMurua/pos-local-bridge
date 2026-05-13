namespace PosLocalBridge.Hardware.Printers.Configuration;

public sealed class PrinterConfig
{
    public string Type { get; set; } = "Tcp";

    public string Address { get; set; } = "";

    public int PortOrBaud { get; set; } = 9100;
}

public sealed class PrinterRegistryConfig
{
    public Dictionary<string, PrinterConfig> Printers { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
