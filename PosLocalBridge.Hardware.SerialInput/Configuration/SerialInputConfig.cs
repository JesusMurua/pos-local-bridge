namespace PosLocalBridge.Hardware.SerialInput.Configuration;

public sealed class SerialDeviceConfig
{
    public string PortName { get; set; } = "";

    public int BaudRate { get; set; } = 9600;
}

public sealed class SerialInputRegistryConfig
{
    public Dictionary<string, SerialDeviceConfig> Devices { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
