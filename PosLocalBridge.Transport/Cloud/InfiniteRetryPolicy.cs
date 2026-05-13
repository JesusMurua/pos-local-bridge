using Microsoft.AspNetCore.SignalR.Client;

namespace PosLocalBridge.Transport.Cloud;

internal sealed class InfiniteRetryPolicy : IRetryPolicy
{
    private const double MaxDelaySeconds = 30.0;

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var attempt = retryContext.PreviousRetryCount + 1;
        var baseSeconds = Math.Min(Math.Pow(2, attempt), MaxDelaySeconds);
        var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2;
        var seconds = Math.Max(0.5, baseSeconds * (1.0 + jitter));
        return TimeSpan.FromSeconds(seconds);
    }
}
