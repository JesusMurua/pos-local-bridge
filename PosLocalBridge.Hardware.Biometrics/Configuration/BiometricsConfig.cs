namespace PosLocalBridge.Hardware.Biometrics.Configuration;

public sealed class BiometricsConfig
{
    /// <summary>
    /// Reserved for future use to switch between Virtual and physical scanners
    /// (e.g., DigitalPersona, ZKTeco). Not consumed by the current implementation.
    /// </summary>
    public string Mode { get; set; } = "Virtual";

    public int ScanIntervalSeconds { get; set; } = 30;
}
