using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PosLocalBridge.Security;

namespace PosLocalBridge.Tests.Security;

public class FileTokenStoreTests : IDisposable
{
    private readonly string _tempPath;
    private readonly FileTokenStore _store;

    public FileTokenStoreTests()
    {
        var logger = Substitute.For<ILogger<FileTokenStore>>();
        _tempPath = Path.Combine(Path.GetTempPath(), $"fino-test-{Guid.NewGuid():N}.auth");
        _store = new FileTokenStore(logger, _tempPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath))
        {
            File.Delete(_tempPath);
        }
    }

    [Fact]
    public async Task SaveAndGet_Roundtrip_ReturnsOriginalToken()
    {
        await _store.SaveTokenAsync("test-jwt");

        var result = await _store.GetTokenAsync();

        result.Should().Be("test-jwt");
    }

    [Fact]
    public async Task ClearTokenAsync_RemovesPersistedToken_NextGetReturnsNull()
    {
        await _store.SaveTokenAsync("test-jwt");
        await _store.ClearTokenAsync();

        var result = await _store.GetTokenAsync();

        result.Should().BeNull();
    }
}
