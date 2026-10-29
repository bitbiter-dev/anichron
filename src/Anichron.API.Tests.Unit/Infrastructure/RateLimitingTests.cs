using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using System.Net;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class RateLimitingTests
{
    [Fact]
    public void PartitionKey_RemoteAddressKnown_IsTheAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        RateLimiting.PartitionKey(context).Should().Be("203.0.113.5");
    }

    [Fact]
    public void PartitionKey_RemoteAddressUnknown_SharesTheUnresolvedBucket()
    {
        RateLimiting.PartitionKey(new DefaultHttpContext()).Should().Be("unresolved");
    }
}
