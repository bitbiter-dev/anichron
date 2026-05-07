using Anichron.API.Security;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Anichron.API.Tests.Unit.Security;

public sealed class PwnedPasswordClientTests
{
    // SHA1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
    // prefix = 5BAA6, suffix = 1E4C9B93F3F0682250B6CF8331B7EE68FD8

    [Theory]
    [InlineData("1E4C9B93F3F0682250B6CF8331B7EE68FD8:1")]
    [InlineData("1e4c9b93f3f0682250b6cf8331b7ee68fd8:1")]
    [InlineData("AAAAA:5\r\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:1")]
    [InlineData("AAAAA:5\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:1")]  // LF-only line endings
    public async Task IsPwnedAsync_PasswordHashFoundInResponse_ReturnsTrue(string responseBody)
    {
        var testee = new TestFixture()
            .WithHttpResponseBody(responseBody)
            .CreateTestee();

        var result = await testee.IsPwnedAsync("password", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAA:5")]
    [InlineData("1E4C9B93F3F0682250B6CF8331B7EE68FD9:1")]
    public async Task IsPwnedAsync_PasswordHashNotFoundInResponse_ReturnsFalse(string responseBody)
    {
        var testee = new TestFixture()
            .WithHttpResponseBody(responseBody)
            .CreateTestee();

        var result = await testee.IsPwnedAsync("password", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPwnedAsync_HttpClientThrows_ReturnsFalse()
    {
        var testee = new TestFixture()
            .WithHttpThrowing()
            .CreateTestee();

        var result = await testee.IsPwnedAsync("password", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPwnedAsync_HttpClientThrows_LogsWarning()
    {
        var fixture = new TestFixture().WithHttpThrowing();
        var testee = fixture.CreateTestee();

        await testee.IsPwnedAsync("password", CancellationToken.None);

        Assert.Multiple(
            () => fixture.Logger.Entries.Should().HaveCount(1),
            () => fixture.Logger.Entries[0].Level.Should().Be(LogLevel.Warning),
            () => fixture.Logger.Entries[0].Exception.Should().BeOfType<HttpRequestException>());
    }

    private sealed class TestFixture : IDisposable
    {
        private HttpMessageHandler _handler = new FakeHttpMessageHandler(string.Empty);
        public CapturingLogger Logger { get; } = new();

        public TestFixture WithHttpResponseBody(string body)
        {
            _handler.Dispose();
            _handler = new FakeHttpMessageHandler(body);
            return this;
        }

        public TestFixture WithHttpThrowing()
        {
            _handler.Dispose();
            _handler = new ThrowingHttpMessageHandler();
            return this;
        }

        public PwnedPasswordClient CreateTestee()
        {
            var http = new HttpClient(_handler) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") };
            return new PwnedPasswordClient(http, Logger);
        }

        public void Dispose() => _handler.Dispose();
    }

    private sealed class FakeHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) });
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("Network unavailable"));
    }

    private sealed class CapturingLogger : ILogger<PwnedPasswordClient>
    {
        private readonly List<(LogLevel Level, Exception? Exception)> _entries = [];
        public IReadOnlyList<(LogLevel Level, Exception? Exception)> Entries => _entries;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Add((logLevel, exception));

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
