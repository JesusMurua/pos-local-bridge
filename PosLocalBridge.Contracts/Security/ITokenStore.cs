namespace PosLocalBridge.Contracts.Security;

public interface ITokenStore
{
    Task<string?> GetTokenAsync();

    Task SaveTokenAsync(string token);

    Task ClearTokenAsync();
}
