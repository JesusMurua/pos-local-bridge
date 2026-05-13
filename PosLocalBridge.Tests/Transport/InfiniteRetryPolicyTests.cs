using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using PosLocalBridge.Transport.Cloud;

namespace PosLocalBridge.Tests.Transport;

public class InfiniteRetryPolicyTests
{
    [Fact]
    public void NextRetryDelay_ShouldNeverReturnNull_AcrossOneThousandAttempts()
    {
        var policy = new InfiniteRetryPolicy();

        for (var i = 0; i <= 1000; i++)
        {
            var context = new RetryContext { PreviousRetryCount = i };
            var result = policy.NextRetryDelay(context);

            result.Should().NotBeNull($"retry attempt {i} must never return null (SignalR would give up)");
        }
    }

    [Fact]
    public void NextRetryDelay_ShouldBeCappedAt30SecondsPlus20PctJitter()
    {
        var policy = new InfiniteRetryPolicy();
        var maxAllowed = TimeSpan.FromSeconds(36);

        for (var i = 0; i <= 1000; i++)
        {
            var context = new RetryContext { PreviousRetryCount = i };
            var result = policy.NextRetryDelay(context);

            result.Should().NotBeNull();
            result!.Value.Should().BeLessThanOrEqualTo(maxAllowed,
                $"retry attempt {i} exceeded the 30s + 20% jitter cap");
        }
    }
}
