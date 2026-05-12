using Anichron.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class HealthCheckResponseWriterTests
{
    private static DefaultHttpContext BuildContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static HealthReport BuildReport(params (string name, HealthStatus status)[] entries)
    {
        var dict = entries.ToDictionary(
            e => e.name,
            e => new HealthReportEntry(e.status, description: null, TimeSpan.Zero, exception: null, data: null));
        return new HealthReport(dict, TimeSpan.Zero);
    }

    private static async Task<string> ReadBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(response.Body).ReadToEndAsync();
    }

    [Fact]
    public async Task WriteResponseAsync_AllHealthy_WritesCorrectContentTypeAndJson()
    {
        var context = BuildContext();
        var report = BuildReport(("database", HealthStatus.Healthy), ("proxyStorage", HealthStatus.Healthy));

        await HealthCheckResponseWriter.WriteResponseAsync(context, report);
        var body = await ReadBodyAsync(context.Response);

        context.Response.ContentType.Should().Be("application/json; charset=utf-8");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        doc.RootElement.GetProperty("components").GetProperty("database").GetString().Should().Be("healthy");
        doc.RootElement.GetProperty("components").GetProperty("proxyStorage").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task WriteResponseAsync_UnhealthyComponent_WritesUnhealthyOverallStatus()
    {
        var context = BuildContext();
        var report = BuildReport(("database", HealthStatus.Unhealthy), ("proxyStorage", HealthStatus.Healthy));

        await HealthCheckResponseWriter.WriteResponseAsync(context, report);
        var body = await ReadBodyAsync(context.Response);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("unhealthy");
        doc.RootElement.GetProperty("components").GetProperty("database").GetString().Should().Be("unhealthy");
        doc.RootElement.GetProperty("components").GetProperty("proxyStorage").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task WriteResponseAsync_DegradedComponent_WritesDegradedStatus()
    {
        var context = BuildContext();
        var report = BuildReport(("database", HealthStatus.Degraded));

        await HealthCheckResponseWriter.WriteResponseAsync(context, report);
        var body = await ReadBodyAsync(context.Response);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetString().Should().Be("degraded");
        doc.RootElement.GetProperty("components").GetProperty("database").GetString().Should().Be("degraded");
    }

    [Fact]
    public async Task WriteResponseAsync_Always_SetsCacheControlNoStore()
    {
        var context = BuildContext();
        var report = BuildReport(("database", HealthStatus.Healthy));

        await HealthCheckResponseWriter.WriteResponseAsync(context, report);

        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }
}
