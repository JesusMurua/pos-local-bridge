using PosLocalBridge.Contracts.Cloud;
using PosLocalBridge.Contracts.Hardware;
using PosLocalBridge.Contracts.Security;
using PosLocalBridge.Hardware.AccessControl;
using PosLocalBridge.Hardware.AccessControl.Configuration;
using PosLocalBridge.Hardware.Biometrics;
using PosLocalBridge.Hardware.Biometrics.Configuration;
using PosLocalBridge.Hardware.Printers;
using PosLocalBridge.Hardware.Printers.Configuration;
using PosLocalBridge.Hardware.Printers.Connections;
using PosLocalBridge.Hardware.SerialInput;
using PosLocalBridge.Hardware.SerialInput.Configuration;
using PosLocalBridge.Host;
using PosLocalBridge.Security;
using PosLocalBridge.Security.Configuration;
using PosLocalBridge.Transport.Cloud;
using PosLocalBridge.Transport.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService();

builder.Services.Configure<BridgeConfig>(builder.Configuration.GetSection("Bridge"));
builder.Services.Configure<SecurityConfig>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<TurnstileConfig>(builder.Configuration.GetSection("Hardware:Turnstile"));
builder.Services.Configure<PrinterRegistryConfig>(builder.Configuration.GetSection("Hardware:PrinterRegistry"));
builder.Services.Configure<BiometricsConfig>(builder.Configuration.GetSection("Hardware:Biometrics"));
builder.Services.Configure<SerialInputRegistryConfig>(builder.Configuration.GetSection("Hardware:SerialInputRegistry"));

builder.Services.AddSingleton<ITokenStore, FileTokenStore>();
builder.Services.AddHttpClient<IPairingService, HttpPairingService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<ICloudClient, FinoCloudClient>();
builder.Services.AddSingleton<ITurnstileController, ComPortTurnstileController>();
builder.Services.AddSingleton<IPrinterConnectionFactory, PrinterConnectionFactory>();
builder.Services.AddSingleton<IPrinterRouter, EscPosRouter>();
builder.Services.AddSingleton<IBiometricScanner, VirtualBiometricScanner>();
builder.Services.AddSingleton<ISerialInputScanner, ContinuousSerialScanner>();

// Registration order matters: supervisors must subscribe before Worker
// kicks off the SignalR connect loop.
builder.Services.AddHostedService<TurnstileSupervisor>();
builder.Services.AddHostedService<PrinterSupervisor>();
builder.Services.AddHostedService<BiometricSupervisor>();
builder.Services.AddHostedService<SerialInputSupervisor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
