namespace PosLocalBridge.Hardware.AccessControl.Configuration;

public sealed class TurnstileConfig
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    public string HexPayload { get; set; } = "";
    public int WriteTimeoutMs { get; set; } = 500;
}
