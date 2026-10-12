using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class RateLimitingTests
{
    private sealed class TestLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames
            => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = retryAfter;
            return retryAfter is not null && metadataName == MetadataName.RetryAfter.Name;
        }
    }

    private static OnRejectedContext Rejected(TimeSpan? retryAfter)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return new OnRejectedContext { HttpContext = httpContext, Lease = new TestLease(retryAfter) };
    }

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

    [Fact]
    public async Task OnRejectedAsync_Always_Writes429WithErrorBodyAsync()
    {
        var context = Rejected(retryAfter: null);

        await RateLimiting.OnRejectedAsync(context, TestContext.Current.CancellationToken);

        var response = context.HttpContext.Response;
        response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.RootElement.GetProperty("error").GetString()
            .Should().Be("Too many requests. Please try again later.");
    }

    [Fact]
    public async Task OnRejectedAsync_LeaseCarriesRetryAfter_SetsHeaderInWholeSecondsAsync()
    {
        var context = Rejected(TimeSpan.FromSeconds(12.7));

        await RateLimiting.OnRejectedAsync(context, TestContext.Current.CancellationToken);

        context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("12");
    }

    [Fact]
    public async Task OnRejectedAsync_LeaseWithoutRetryAfter_OmitsHeaderAsync()
    {
        var context = Rejected(retryAfter: null);

        await RateLimiting.OnRejectedAsync(context, TestContext.Current.CancellationToken);

        context.HttpContext.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }
}
