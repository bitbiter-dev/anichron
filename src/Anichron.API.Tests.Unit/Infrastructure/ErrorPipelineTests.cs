using Anichron.API.Infrastructure;
using Anichron.API.Tests.Unit.Endpoints;
using Anichron.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Npgsql;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Anichron.API.Tests.Unit.Infrastructure;

public sealed class ErrorPipelineTests
{
    private const string Unavailable = "The service is temporarily unavailable. Try again shortly.";

    private readonly FakeLogCollector logs = new();

    private Task<AuthenticatedTestApp> StartThrowingAsync(Exception exception)
        => AuthenticatedTestApp.StartAsync(
            api => api.MapGet("/boom", IResult () => throw exception).AllowAnonymous(),
            services => services.AddLogging(logging => logging.AddProvider(new FakeLoggerProvider(logs))));

    private static Task<HttpResponseMessage> GetBoomAsync(AuthenticatedTestApp app)
        => app.Client.GetAsync("/api/v1/boom", TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnUnhandledException_IsAServerErrorProblemAsync()
    {
        await using var app = await StartThrowingAsync(new InvalidOperationException("boom"));

        using var response = await GetBoomAsync(app);

        await ProblemAssertions.AssertStatusProblemAsync(response, 500);
        response.Headers.RetryAfter.Should().BeNull();
    }

    public static TheoryData<string> Outages => ["refused", "timeout", "save", "shutdown"];

    private static Exception Outage(string kind) => kind switch
    {
        "refused" => new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)),
        "timeout" => new NpgsqlException("Exception while reading from stream", new TimeoutException()),
        "save" => new DbUpdateException("An error occurred while saving", new NpgsqlException("Failed to connect", new IOException())),
        "shutdown" => new PostgresException("terminating connection due to administrator command", "FATAL", "FATAL", "57P01"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task AnUnreachableDatabase_IsAServiceUnavailableProblemAsync(string kind)
    {
        await using var app = await StartThrowingAsync(Outage(kind));

        using var response = await GetBoomAsync(app);

        await ProblemAssertions.AssertProblemAsync(response, 503, "service_unavailable", Unavailable);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AConstraintViolation_IsStillAServerErrorAsync()
    {
        await using var app = await StartThrowingAsync(
            new DbUpdateException("An error occurred while saving", new PostgresException("duplicate key", "ERROR", "ERROR", "23505")));

        using var response = await GetBoomAsync(app);

        await ProblemAssertions.AssertStatusProblemAsync(response, 500);
    }

    [Fact]
    public async Task AnUnreachableDatabase_LogsAWarningWithoutTheStackTraceAsync()
    {
        await using var app = await StartThrowingAsync(Outage("refused"));

        using var _ = await GetBoomAsync(app);

        var record = logs.GetSnapshot().Should().ContainSingle(entry => entry.Level >= LogLevel.Warning).Which;
        record.Level.Should().Be(LogLevel.Warning);
        record.Exception.Should().BeNull();
        record.Message.Should().Be("GET /api/v1/boom answered 503: the database is unreachable (Failed to connect).");
    }

    [Fact]
    public async Task AServerError_LogsAnErrorWithTheExceptionAsync()
    {
        var exception = new InvalidOperationException("boom");
        await using var app = await StartThrowingAsync(exception);

        using var _ = await GetBoomAsync(app);

        var record = logs.GetSnapshot().Should().ContainSingle(entry => entry.Level >= LogLevel.Warning).Which;
        record.Level.Should().Be(LogLevel.Error);
        record.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task AQueryAgainstAClosedPort_IsAServiceUnavailableProblemAsync()
    {
        var database = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["POSTGRES_CONNECTION:HOST"] = IPAddress.Loopback.ToString(),
            ["POSTGRES_CONNECTION:PORT"] = ClosedPort().ToString(CultureInfo.InvariantCulture),
            ["POSTGRES_CONNECTION:DBNAME"] = "anichron",
            ["POSTGRES_CONNECTION:USER"] = "app",
            ["POSTGRES_CONNECTION:PASSWORD"] = "pw",
        }).Build();
        await using var app = await AuthenticatedTestApp.StartAsync(
            api => api.MapGet("/boom", async (AnichronDbContext db, CancellationToken ct) =>
                TypedResults.Ok(await db.Users.AnyAsync(ct))).AllowAnonymous(),
            services => services.AddDatabase(database));

        using var response = await GetBoomAsync(app);

        await ProblemAssertions.AssertProblemAsync(response, 503, "service_unavailable", Unavailable);
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
